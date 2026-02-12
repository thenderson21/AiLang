using AiLang.Core;
using AiVM.Core;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace AiLang.Tests;

public class AosTests
{
    private sealed class RecordingSyscallHost : DefaultSyscallHost
    {
        public string? LastStdoutLine { get; private set; }
        public int IoPrintCount { get; private set; }

        public override void StdoutWriteLine(string text)
        {
            LastStdoutLine = text;
        }

        public override void IoPrint(string text)
        {
            IoPrintCount++;
        }

        public override int StrUtf8ByteCount(string text)
        {
            return 777;
        }
    }

    [Test]
    public void CompilerAssets_CanFindBundledCompilerFiles()
    {
        var path = AosCompilerAssets.TryFind("runtime.aos");
        Assert.That(path, Is.Not.Null);
        Assert.That(path!.EndsWith("runtime.aos", StringComparison.Ordinal), Is.True);
    }

    [Test]
    public void CompilerAssets_LoadRequiredProgram_ReturnsProgramRoot()
    {
        var program = AosCompilerAssets.LoadRequiredProgram("format.aos");
        Assert.That(program.Kind, Is.EqualTo("Program"));
        Assert.That(program.Children.Count, Is.GreaterThan(0));
    }

    [Test]
    public void TokenizerParser_ParsesBasicProgram()
    {
        var source = "Program#p1 { // root\n Let#l1(name=answer) { Lit#v1(value=1) } }";
        var parse = Parse(source);
        Assert.That(parse.Root, Is.Not.Null);
        Assert.That(parse.Diagnostics, Is.Empty);

        var program = parse.Root!;
        Assert.That(program.Kind, Is.EqualTo("Program"));
        Assert.That(program.Children.Count, Is.EqualTo(1));

        var letNode = program.Children[0];
        Assert.That(letNode.Kind, Is.EqualTo("Let"));
        Assert.That(letNode.Attrs["name"].AsString(), Is.EqualTo("answer"));
        Assert.That(letNode.Children.Count, Is.EqualTo(1));
        Assert.That(letNode.Children[0].Kind, Is.EqualTo("Lit"));
    }

    [Test]
    public void Formatter_RoundTrips()
    {
        var source = "Call#c1(target=console.print) { Lit#s1(value=\"hi\\nthere\") }";
        var parse1 = Parse(source);
        Assert.That(parse1.Diagnostics, Is.Empty);
        var formatted = AosFormatter.Format(parse1.Root!);
        var parse2 = Parse(formatted);
        Assert.That(parse2.Diagnostics, Is.Empty);
        var formatted2 = AosFormatter.Format(parse2.Root!);
        Assert.That(formatted2, Is.EqualTo(formatted));
    }

    [Test]
    public void Formatter_ProducesCanonicalOutput()
    {
        var source = "Call#c1(target=console.print) { Lit#s1(value=\"hi\") }";
        var parse = Parse(source);
        Assert.That(parse.Diagnostics, Is.Empty);
        var formatted = AosFormatter.Format(parse.Root!);
        Assert.That(formatted, Is.EqualTo("Call#c1(target=console.print) { Lit#s1(value=\"hi\") }"));
    }

    [Test]
    public void Formatter_SortsAttributesByKey()
    {
        var source = "Program#p1(z=1 a=2) { }";
        var parse = Parse(source);
        Assert.That(parse.Diagnostics, Is.Empty);
        var formatted = AosFormatter.Format(parse.Root!);
        Assert.That(formatted, Is.EqualTo("Program#p1(a=2 z=1)"));
    }

    [Test]
    public void Validator_DeniesConsolePermission()
    {
        var source = "Call#c1(target=console.print) { Lit#s1(value=\"hi\") }";
        var parse = Parse(source);
        var validator = new AosValidator();
        var permissions = new HashSet<string>(StringComparer.Ordinal) { "math" };
        var result = validator.Validate(parse.Root!, null, permissions);
        Assert.That(result.Diagnostics.Any(d => d.Code == "VAL040"), Is.True);
    }

    [Test]
    public void Validator_ReportsMissingAttribute()
    {
        var source = "Let#l1 { Lit#v1(value=1) }";
        var parse = Parse(source);
        var validator = new AosValidator();
        var permissions = new HashSet<string>(StringComparer.Ordinal) { "math" };
        var result = validator.Validate(parse.Root!, null, permissions);
        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Code == "VAL002");
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic!.Message, Is.EqualTo("Missing attribute 'name'."));
        Assert.That(diagnostic.NodeId, Is.EqualTo("l1"));
    }

    [Test]
    public void Validator_ReportsDuplicateIds()
    {
        var source = "Program#p1 { Lit#dup(value=1) Lit#dup(value=2) }";
        var parse = Parse(source);
        var validator = new AosValidator();
        var permissions = new HashSet<string>(StringComparer.Ordinal) { "math" };
        var result = validator.Validate(parse.Root!, null, permissions);
        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Code == "VAL001");
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic!.Message, Is.EqualTo("Duplicate node id 'dup'."));
        Assert.That(diagnostic.NodeId, Is.EqualTo("dup"));
    }

    [Test]
    public void PatchOps_ReplaceAndInsert()
    {
        var program = Parse("Program#p1 { Let#l1(name=x) { Lit#v1(value=1) } }").Root!;
        var replaceOp = Parse("Op#o1(kind=replaceNode id=v1) { Lit#v2(value=2) }").Root!;
        var insertOp = Parse("Op#o2(kind=insertChild parentId=p1 slot=declarations index=1) { Let#l2(name=y) { Lit#v3(value=3) } }").Root!;

        var applier = new AosPatchApplier();
        var result = applier.Apply(program, new[] { replaceOp, insertOp });

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.Root!.Children.Count, Is.EqualTo(2));
        var updatedLit = result.Root.Children[0].Children[0];
        Assert.That(updatedLit.Attrs["value"].AsInt(), Is.EqualTo(2));
    }

    [Test]
    public void Repl_PersistsStateAcrossCommands()
    {
        var session = new AosReplSession();
        var load = "Cmd#c1(name=load) { Program#p1 { Let#l1(name=foo) { Lit#v1(value=5) } } }";
        var eval = "Cmd#c2(name=eval) { Var#v2(name=foo) }";

        var loadResult = session.ExecuteLine(load);
        Assert.That(loadResult.Contains("Ok#"), Is.True);

        var evalResult = session.ExecuteLine(eval);
        Assert.That(evalResult.Contains("type=int"), Is.True);
        Assert.That(evalResult.Contains("value=5"), Is.True);
    }

    [Test]
    public void Repl_HelpReturnsStructuredCommands()
    {
        var session = new AosReplSession();
        var help = "Cmd#c1(name=help)";
        var result = session.ExecuteLine(help);

        var parse = Parse(result);
        Assert.That(parse.Diagnostics, Is.Empty);
        Assert.That(parse.Root, Is.Not.Null);

        var ok = parse.Root!;
        Assert.That(ok.Kind, Is.EqualTo("Ok"));
        Assert.That(ok.Attrs["type"].AsString(), Is.EqualTo("void"));
        Assert.That(ok.Children.Count, Is.EqualTo(5));
        Assert.That(ok.Children.All(child => child.Kind == "Cmd"), Is.True);
        var names = ok.Children.Select(child => child.Attrs["name"].AsString()).ToList();
        var expected = new[] { "help", "setPerms", "load", "eval", "applyPatch" };
        Assert.That(names.Count, Is.EqualTo(expected.Length));
        Assert.That(expected.All(name => names.Contains(name)), Is.True);
    }

    [Test]
    public void CompilerBuiltins_ParseAndValidateWork()
    {
        var source = "Program#p1 { Call#c1(target=compiler.validate) { Call#c2(target=compiler.parse) { Lit#s1(value=\"Program#p2 { Lit#l1(value=1) }\") } } }";
        var parse = Parse(source);
        Assert.That(parse.Diagnostics, Is.Empty);

        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        var validator = new AosValidator();
        var validation = validator.Validate(parse.Root!, null, runtime.Permissions, runStructural: false);
        Assert.That(validation.Diagnostics, Is.Empty);

        var interpreter = new AosInterpreter();
        var value = interpreter.EvaluateProgram(parse.Root!, runtime);
        Assert.That(value.Kind, Is.EqualTo(AosValueKind.Node));
        var diags = value.AsNode();
        Assert.That(diags.Kind, Is.EqualTo("Block"));
        Assert.That(diags.Children.Count, Is.EqualTo(0));
    }

    [Test]
    public void CompilerEmitBytecode_IncludesHeaderFields()
    {
        var source = "Program#p1 { Call#c1(target=compiler.emitBytecode) { Call#c2(target=compiler.parse) { Lit#s1(value=\"Program#p2 { Lit#l1(value=1) }\") } } }";
        var parse = Parse(source);
        Assert.That(parse.Diagnostics, Is.Empty);

        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        var interpreter = new AosInterpreter();
        var value = interpreter.EvaluateProgram(parse.Root!, runtime);

        Assert.That(value.Kind, Is.EqualTo(AosValueKind.Node));
        var bytecode = value.AsNode();
        Assert.That(bytecode.Kind, Is.EqualTo("Bytecode"));
        Assert.That(bytecode.Attrs["magic"].AsString(), Is.EqualTo("AIBC"));
        Assert.That(bytecode.Attrs["format"].AsString(), Is.EqualTo("AiBC1"));
        Assert.That(bytecode.Attrs["version"].AsInt(), Is.EqualTo(1));
        Assert.That(bytecode.Attrs["flags"].AsInt(), Is.EqualTo(0));
    }

    [Test]
    public void SyscallDispatch_Utf8Count_ReturnsInt()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.str_utf8ByteCount) { Lit#s1(value=\"abc\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var runtime = new AosRuntime();
        runtime.Permissions.Add("sys");
        var interpreter = new AosInterpreter();
        var value = interpreter.EvaluateProgram(parse.Root!, runtime);

        Assert.That(value.Kind, Is.EqualTo(AosValueKind.Int));
        Assert.That(value.AsInt(), Is.EqualTo(3));
    }

    [Test]
    public void VmSyscalls_UsesConfiguredHost()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;

            var count = VmSyscalls.StrUtf8ByteCount("abc");
            VmSyscalls.StdoutWriteLine("hello");

            Assert.That(count, Is.EqualTo(777));
            Assert.That(host.LastStdoutLine, Is.EqualTo("hello"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void UnknownSysTarget_DoesNotEvaluateArguments()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.unknown) { Call#c2(target=io.print) { Lit#s1(value=\"side-effect\") } } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            runtime.Permissions.Add("io");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);

            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Unknown));
            Assert.That(host.IoPrintCount, Is.EqualTo(0));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void InvalidCapabilityArity_DoesNotEvaluateArguments()
    {
        var parse = Parse("Program#p1 { Call#c1(target=io.print) { Call#c2(target=io.print) { Lit#s1(value=\"nested\") } Lit#s2(value=\"extra\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("io");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);

            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Unknown));
            Assert.That(host.IoPrintCount, Is.EqualTo(0));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_InvalidArgs_ReturnsUnknown()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.net_close) }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var runtime = new AosRuntime();
        runtime.Permissions.Add("sys");
        var interpreter = new AosInterpreter();
        var value = interpreter.EvaluateProgram(parse.Root!, runtime);

        Assert.That(value.Kind, Is.EqualTo(AosValueKind.Unknown));
    }

    [Test]
    public void VmRunBytecode_RejectsUnsupportedVersion()
    {
        var bytecode = Parse("Bytecode#bc1(magic=\"AIBC\" format=\"AiBC1\" version=2 flags=0)").Root!;
        var runtime = new AosRuntime();
        var interpreter = new AosInterpreter();
        var args = Parse("Block#argv").Root!;

        var value = interpreter.RunBytecode(bytecode, "main", args, runtime);
        Assert.That(value.Kind, Is.EqualTo(AosValueKind.Node));
        var err = value.AsNode();
        Assert.That(err.Kind, Is.EqualTo("Err"));
        Assert.That(err.Attrs["code"].AsString(), Is.EqualTo("VM001"));
        Assert.That(err.Attrs["message"].AsString(), Is.EqualTo("Unsupported bytecode version."));
    }

    [Test]
    public void VmRunBytecode_EmitsInstructionTrace_WhenEnabled()
    {
        var source = "Program#p1 { Call#c1(target=compiler.emitBytecode) { Call#c2(target=compiler.parse) { Lit#s1(value=\"Program#p2 { Lit#l1(value=1) }\") } } }";
        var parse = Parse(source);
        Assert.That(parse.Diagnostics, Is.Empty);

        var interpreter = new AosInterpreter();
        var runtime = new AosRuntime { TraceEnabled = true };
        runtime.Permissions.Add("compiler");
        var bytecodeValue = interpreter.EvaluateProgram(parse.Root!, runtime);
        Assert.That(bytecodeValue.Kind, Is.EqualTo(AosValueKind.Node));

        runtime.TraceSteps.Clear();
        var args = Parse("Block#argv").Root!;
        var result = interpreter.RunBytecode(bytecodeValue.AsNode(), "main", args, runtime);
        Assert.That(result.Kind, Is.Not.EqualTo(AosValueKind.Unknown));

        var vmSteps = runtime.TraceSteps
            .Where(step => step.Kind == "Step" &&
                           step.Attrs.TryGetValue("kind", out var kindAttr) &&
                           kindAttr.Kind == AosAttrKind.String &&
                           kindAttr.AsString() == "VmInstruction")
            .ToList();

        Assert.That(vmSteps.Count, Is.GreaterThan(0));
        Assert.That(vmSteps.Any(step =>
            step.Attrs.TryGetValue("op", out var opAttr) &&
            opAttr.Kind == AosAttrKind.String &&
            opAttr.AsString() == "RETURN"), Is.True);
    }

    [Test]
    public void Evaluator_ImportsAndMergesExplicitExports()
    {
        var tempDir = Directory.CreateTempSubdirectory("ailang-import-ok-");
        try
        {
            var depPath = Path.Combine(tempDir.FullName, "dep.aos");
            File.WriteAllText(depPath, "Program#dp { Let#dl(name=x) { Lit#dv(value=7) } Export#de(name=x) }");

            var main = Parse("Program#mp { Import#mi(path=\"dep.aos\") Var#mv(name=x) }").Root!;
            var runtime = new AosRuntime { ModuleBaseDir = tempDir.FullName };
            var interpreter = new AosInterpreter();
            var result = interpreter.EvaluateProgram(main, runtime);

            Assert.That(result.Kind, Is.EqualTo(AosValueKind.Int));
            Assert.That(result.AsInt(), Is.EqualTo(7));
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Test]
    public void Evaluator_ReportsCircularImport()
    {
        var tempDir = Directory.CreateTempSubdirectory("ailang-import-cycle-");
        try
        {
            File.WriteAllText(Path.Combine(tempDir.FullName, "a.aos"), "Program#a { Import#ia(path=\"b.aos\") Let#al(name=x) { Lit#av(value=1) } Export#ae(name=x) }");
            File.WriteAllText(Path.Combine(tempDir.FullName, "b.aos"), "Program#b { Import#ib(path=\"a.aos\") Let#bl(name=y) { Lit#bv(value=2) } Export#be(name=y) }");

            var main = Parse("Program#mp { Import#mi(path=\"a.aos\") Var#mv(name=x) }").Root!;
            var runtime = new AosRuntime { ModuleBaseDir = tempDir.FullName };
            var interpreter = new AosInterpreter();
            var result = interpreter.EvaluateProgram(main, runtime);

            Assert.That(result.Kind, Is.EqualTo(AosValueKind.Node));
            var err = result.AsNode();
            Assert.That(err.Kind, Is.EqualTo("Err"));
            Assert.That(err.Attrs["code"].AsString(), Is.EqualTo("RUN023"));
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Test]
    public void Evaluator_ReportsMissingImportFile()
    {
        var tempDir = Directory.CreateTempSubdirectory("ailang-import-missing-");
        try
        {
            var main = Parse("Program#mp { Import#mi(path=\"missing.aos\") }").Root!;
            var runtime = new AosRuntime { ModuleBaseDir = tempDir.FullName };
            var interpreter = new AosInterpreter();
            var result = interpreter.EvaluateProgram(main, runtime);

            Assert.That(result.Kind, Is.EqualTo(AosValueKind.Node));
            var err = result.AsNode();
            Assert.That(err.Kind, Is.EqualTo("Err"));
            Assert.That(err.Attrs["code"].AsString(), Is.EqualTo("RUN024"));
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Test]
    public void CompilerRun_ReturnsErrNode_ForMissingImport()
    {
        var tempDir = Directory.CreateTempSubdirectory("ailang-compiler-run-missing-");
        try
        {
            var source = "Program#p1 { Let#l1(name=parsed) { Call#c1(target=compiler.parse) { Lit#s1(value=\"Program#p2 { Import#i1(path=\\\"missing.aos\\\") }\") } } Call#c2(target=compiler.run) { Var#v1(name=parsed) } }";
            var root = Parse(source).Root!;
            var runtime = new AosRuntime { ModuleBaseDir = tempDir.FullName };
            runtime.Permissions.Add("compiler");

            var interpreter = new AosInterpreter();
            var result = interpreter.EvaluateProgram(root, runtime);
            Assert.That(result.Kind, Is.EqualTo(AosValueKind.Node));
            Assert.That(result.AsNode().Kind, Is.EqualTo("Err"));
            Assert.That(result.AsNode().Attrs["code"].AsString(), Is.EqualTo("RUN024"));
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Test]
    public void Aic_Smoke_FmtCheckRun()
    {
        var aicPath = FindRepoFile("src/compiler/aic.aos");
        var fmtInput = File.ReadAllText(FindRepoFile("examples/aic_fmt_input.aos"));
        var checkInput = File.ReadAllText(FindRepoFile("examples/aic_check_bad.aos"));
        var runInput = File.ReadAllText(FindRepoFile("examples/aic_run_input.aos"));

        var fmtOutput = ExecuteAic(aicPath, "fmt", fmtInput);
        Assert.That(fmtOutput, Is.EqualTo("Program#p1 { Let#l1(name=x) { Lit#v1(value=1) } }"));

        var checkOutput = ExecuteAic(aicPath, "check", checkInput);
        Assert.That(checkOutput.Contains("Err#diag0(code=VAL002"), Is.True);

        var runOutput = ExecuteAic(aicPath, "run", runInput);
        Assert.That(runOutput, Is.EqualTo("Ok#ok0(type=string value=\"hello from main\")"));
    }

    [Test]
    public void Aic_Run_ImportDiagnostics()
    {
        var aicPath = FindRepoFile("src/compiler/aic.aos");
        var cycleInput = File.ReadAllText(FindRepoFile("examples/golden/run_import_cycle.in.aos"));
        var missingInput = File.ReadAllText(FindRepoFile("examples/golden/run_import_missing.in.aos"));

        var cycleOutput = ExecuteAic(aicPath, "run", cycleInput);
        var missingOutput = ExecuteAic(aicPath, "run", missingInput);

        Assert.That(cycleOutput, Is.EqualTo("Err#runtime_err(code=RUN023 message=\"Circular import detected.\" nodeId=mb2)"));
        Assert.That(missingOutput, Is.EqualTo("Err#runtime_err(code=RUN024 message=\"Import file not found: examples/golden/modules/does_not_exist.aos\" nodeId=rm2)"));
    }

    [Test]
    public void Serve_HealthEndpoint_ReturnsOk()
    {
        var appPath = FindRepoFile("examples/golden/http/health_app.aos");
        var repoRoot = Path.GetDirectoryName(FindRepoFile("AiLang.slnx"))!;
        var port = FindFreePort();

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(Path.Combine(repoRoot, "src", "AiCLI"));
        psi.ArgumentList.Add("serve");
        psi.ArgumentList.Add(appPath);
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));

        using var process = Process.Start(psi);
        Assert.That(process, Is.Not.Null);

        try
        {
            var response = SendHttpRequest(port, "GET /health HTTP/1.1\r\nHost: localhost\r\n\r\n");
            Assert.That(response.Contains("HTTP/1.1 200 OK", StringComparison.Ordinal), Is.True);
            Assert.That(response.EndsWith("{\"status\":\"ok\"}", StringComparison.Ordinal), Is.True);
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
    }

    [Test]
    public void Serve_HealthEndpoint_ReturnsOk_OverHttps()
    {
        var appPath = FindRepoFile("examples/golden/http/health_app.aos");
        var repoRoot = Path.GetDirectoryName(FindRepoFile("AiLang.slnx"))!;
        var port = FindFreePort();
        var certDir = Directory.CreateTempSubdirectory("ailang-https-");
        var certPath = Path.Combine(certDir.FullName, "cert.pem");
        var keyPath = Path.Combine(certDir.FullName, "key.pem");
        WriteSelfSignedPem(certPath, keyPath);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(Path.Combine(repoRoot, "src", "AiCLI"));
        psi.ArgumentList.Add("serve");
        psi.ArgumentList.Add(appPath);
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--tls-cert");
        psi.ArgumentList.Add(certPath);
        psi.ArgumentList.Add("--tls-key");
        psi.ArgumentList.Add(keyPath);

        using var process = Process.Start(psi);
        Assert.That(process, Is.Not.Null);

        try
        {
            var response = SendHttpsRequest(port, "GET /health HTTP/1.1\r\nHost: localhost\r\n\r\n");
            Assert.That(response.Contains("HTTP/1.1 200 OK", StringComparison.Ordinal), Is.True);
            Assert.That(response.EndsWith("{\"status\":\"ok\"}", StringComparison.Ordinal), Is.True);
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
            certDir.Delete(recursive: true);
        }
    }

    [Test]
    public void Serve_InvalidTlsMaterial_ReturnsRuntimeError()
    {
        var appPath = FindRepoFile("examples/golden/http/health_app.aos");
        var repoRoot = Path.GetDirectoryName(FindRepoFile("AiLang.slnx"))!;
        var port = FindFreePort();
        var missingCert = Path.Combine(Path.GetTempPath(), $"ailang-missing-cert-{Guid.NewGuid():N}.pem");
        var missingKey = Path.Combine(Path.GetTempPath(), $"ailang-missing-key-{Guid.NewGuid():N}.pem");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(Path.Combine(repoRoot, "src", "AiCLI"));
        psi.ArgumentList.Add("serve");
        psi.ArgumentList.Add(appPath);
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--tls-cert");
        psi.ArgumentList.Add(missingCert);
        psi.ArgumentList.Add("--tls-key");
        psi.ArgumentList.Add(missingKey);

        using var process = Process.Start(psi);
        Assert.That(process, Is.Not.Null);

        var exited = process!.WaitForExit(15000);
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("serve did not exit for invalid TLS material.");
        }

        var output = process.StandardOutput.ReadToEnd().Trim();
        Assert.That(process.ExitCode, Is.EqualTo(3), process.StandardError.ReadToEnd());
        Assert.That(
            output,
            Is.EqualTo("Err#runtime_err(code=TLS003 message=\"Failed to load TLS certificate or key.\" nodeId=serve)"));
    }

    [Test]
    public void Bench_Command_ReturnsStructuredReport()
    {
        var repoRoot = Path.GetDirectoryName(FindRepoFile("AiLang.slnx"))!;
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(Path.Combine(repoRoot, "src", "AiCLI"));
        psi.ArgumentList.Add("bench");
        psi.ArgumentList.Add("--iterations");
        psi.ArgumentList.Add("1");

        using var process = Process.Start(psi);
        Assert.That(process, Is.Not.Null);
        var output = process!.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit(15000);

        Assert.That(process.ExitCode, Is.EqualTo(0), process.StandardError.ReadToEnd());
        var parsed = Parse(output);
        Assert.That(parsed.Diagnostics, Is.Empty);
        Assert.That(parsed.Root, Is.Not.Null);
        Assert.That(parsed.Root!.Kind, Is.EqualTo("Benchmark"));
        Assert.That(parsed.Root.Children.Count, Is.EqualTo(5));
    }

    private static AosParseResult Parse(string source)
    {
        var tokenizer = new AosTokenizer(source);
        var tokens = tokenizer.Tokenize();
        var parser = new AosParser(tokens);
        var result = parser.ParseSingle();
        result.Diagnostics.AddRange(tokenizer.Diagnostics);
        return result;
    }

    private static string ExecuteAic(string aicPath, string mode, string stdin)
    {
        var aicProgram = Parse(File.ReadAllText(aicPath)).Root!;
        var runtime = new AosRuntime();
        runtime.Permissions.Add("io");
        runtime.Permissions.Add("compiler");
        runtime.ModuleBaseDir = Path.GetDirectoryName(FindRepoFile("AiLang.slnx"))!;
        runtime.Env["argv"] = AosValue.FromNode(AosRuntimeNodes.BuildArgvNode(new[] { mode }));
        runtime.ReadOnlyBindings.Add("argv");

        var validator = new AosValidator();
        var envTypes = new Dictionary<string, AosValueKind>(StringComparer.Ordinal)
        {
            ["argv"] = AosValueKind.Node
        };
        var validation = validator.Validate(aicProgram, envTypes, runtime.Permissions, runStructural: false);
        Assert.That(validation.Diagnostics, Is.Empty);

        var oldIn = Console.In;
        var oldOut = Console.Out;
        var writer = new StringWriter();
        try
        {
            Console.SetIn(new StringReader(stdin));
            Console.SetOut(writer);
            var interpreter = new AosInterpreter();
            interpreter.EvaluateProgram(aicProgram, runtime);
        }
        finally
        {
            Console.SetIn(oldIn);
            Console.SetOut(oldOut);
        }

        return writer.ToString();
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find {relativePath} from {AppContext.BaseDirectory}");
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string SendHttpRequest(int port, string request)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                client.Connect(IPAddress.Loopback, port);
                client.ReceiveTimeout = 2000;
                client.SendTimeout = 2000;
                using var stream = client.GetStream();
                var bytes = Encoding.ASCII.GetBytes(request);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                last = ex;
                Thread.Sleep(50);
            }
        }

        throw new InvalidOperationException("Failed to connect to serve endpoint.", last);
    }

    private static string SendHttpsRequest(int port, string request)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                client.Connect(IPAddress.Loopback, port);
                client.ReceiveTimeout = 2000;
                client.SendTimeout = 2000;
                using var stream = client.GetStream();
                using var ssl = new SslStream(stream, leaveInnerStreamOpen: false, (_, _, _, _) => true);
                ssl.AuthenticateAsClient(new SslClientAuthenticationOptions
                {
                    TargetHost = "localhost",
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                });

                var bytes = Encoding.ASCII.GetBytes(request);
                ssl.Write(bytes, 0, bytes.Length);
                ssl.Flush();
                using var reader = new StreamReader(ssl, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                last = ex;
                Thread.Sleep(50);
            }
        }

        throw new InvalidOperationException("Failed to connect to HTTPS serve endpoint.", last);
    }

    private static void WriteSelfSignedPem(string certPath, string keyPath)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllText(certPath, cert.ExportCertificatePem());
        File.WriteAllText(keyPath, rsa.ExportPkcs8PrivateKeyPem());
    }
}
