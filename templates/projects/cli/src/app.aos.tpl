Program#p1 {
  Export#e1(name="{{project.entryExport}}")
  Let#l1(name="{{project.entryExport}}") {
    Fn#f1(params="args") {
      Block#b1 {
        Call#c1(target=sys.stdout.writeLine) { Lit#s1(value="Hello from {{project.name}}.") }
        Return#r1 { Lit#i1(value=0) }
      }
    }
  }
}
