Program#p1 {
  Export#e1(name="{{project.entryExport}}")
  Let#l1(name="{{project.entryExport}}") {
    Fn#f1(params="args") {
      Block#b1 {
        Let#l2(name=count) { ChildCount#cc1 { Var#v1(name=args) } }
        If#if1 {
          Eq#eq1 { Var#v2(name=count) Lit#i1(value=0) }
          Block#b2 {
            Call#c1(target=sys.stdout.writeLine) { Lit#s1(value="{{project.name}}: no app args") }
            Return#r1 { Lit#i2(value=0) }
          }
          Block#b3 {
            Call#c2(target=sys.stdout.writeLine) {
              AttrValueString#avs1 {
                ChildAt#ca1 { Var#v3(name=args) Lit#i3(value=0) }
                Lit#i4(value=0)
              }
            }
            Return#r2 { Lit#i5(value=0) }
          }
        }
      }
    }
  }
}
