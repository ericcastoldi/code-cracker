﻿using CodeCracker.CSharp.Design;
using Microsoft.CodeAnalysis;
using System.Threading.Tasks;
using Xunit;
using System;

namespace CodeCracker.Test.CSharp.Design
{
    public class MakeMethodStaticTests : CodeFixVerifier<MakeMethodStaticAnalyzer, MakeMethodStaticCodeFixProvider>
    {
        [Theory]
        [InlineData(@"static void Foo() { }")]
        [InlineData(@"public virtual void Foo() { }")]
        [InlineData(@"string i; void Foo() { Console.WriteLine(i); }")]
        [InlineData(@"string i; void Foo() { i = """"; }")]
        [InlineData(@"string i; string Foo() { return i; }")]
        [InlineData(@"string i;
void Foo()
{
    if (System.DateTime.Now.Seconds > 5)
    {
        Console.WriteLine(i);
    }
}")]
        public async Task NoDiagnostic(string code)
        {
            var source = code.WrapInCSharpClass();
            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        [Fact]
        public async Task NoDiagnosticOnNew()
        {
            const string source = @"
        class B
        {
            private int i = 1;
            public int Foo() => i;
        }
        class C : B
        {
            public new int Foo() => 1;
        }";
            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        [Fact]
        public async Task NoDiagnosticOnOverrideAndAbstract()
        {
            const string source = @"
        abstract class B
        {
            public abstract void Foo();
        }
        class C : B
        {
            public override void Foo() { }
        }";
            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        [Fact]
        public async Task NoDiagnosticOnPartial()
        {
            const string source = @"
        partial class C
        {
            partial void Foo() { }
            partial void Foo();
        }";
            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }

        [Theory]
        [InlineData("void Foo() { }")]
        [InlineData(@"void Foo()
{
    Console.WriteLine(1);
}")]
        [InlineData(@"void Foo()
{
    Console.WriteLine(i);
}
static string i;")]
        [InlineData("void Foo() => Console.WriteLine(1);")]
        public async Task WithDiagnostic(string code)
        {
            var source = code.WrapInCSharpClass();
            var expected = new DiagnosticResult
            {
                Id = DiagnosticId.MakeMethodStatic.ToDiagnosticId(),
                Message = string.Format(MakeMethodStaticAnalyzer.MessageFormat, "Foo"),
                Severity = DiagnosticSeverity.Warning,
                Locations = new[] { new DiagnosticResultLocation("Test0.cs", 8, 18) }
            };
            await VerifyCSharpDiagnosticAsync(source, expected);
        }

        [Theory]
        [InlineData("void Foo() { }", "static void Foo() { }")]
        [InlineData("int Foo() => 1;", "static int Foo() => 1;")]
        public async Task FixMakeMethodStaticWithoutReference(string code, string fix)
        {
            var source = code.WrapInCSharpClass();
            var fixtest = fix.WrapInCSharpClass();
            await VerifyCSharpFixAsync(source, fixtest);
        }

        [Fact]
        public async Task MakeMethodStaticWithReference()
        {
            var source = @"void Foo() { }
void Bar()
{
    Foo();
}".WrapInCSharpClass();
            var fixtest = @"static void Foo() { }
void Bar()
{
    Foo();
}".WrapInCSharpClass();
            await VerifyCSharpFixAsync(source, fixtest);
        }

        [Fact]
        public async Task MakeMethodStaticWithReferenceWithThis()
        {
            var source = @"void Foo() { }
void Bar()
{
    this.Foo();
}".WrapInCSharpClass();
            var fixtest = @"static void Foo() { }
void Bar()
{
    Foo();
}".WrapInCSharpClass();
            await VerifyCSharpFixAsync(source, fixtest);
        }

        [Fact]
        public async Task MakeMethodStaticWithReferenceAndLongNames()
        {
            var source = @"int Foo() => 1;
void Bar()
{
    var result = this.Foo() + this.Foo();
}".WrapInCSharpClass();
            var fixtest = @"static int Foo() => 1;
void Bar()
{
    var result = Foo() + Foo();
}".WrapInCSharpClass();
            await VerifyCSharpFixAsync(source, fixtest);
        }

        [Fact]
        public async Task MakeMethodStaticWithReferenceInDifferentDocs()
        {
            var source1 = @"private int i;
void Bar()
{
    i = 1;
    var t = new Type2();
    t.Foo();
}".WrapInCSharpClass("Type1");
            var source2 = @"public void Foo() { }".WrapInCSharpClass("Type2");
            var fixtest1 = @"private int i;
void Bar()
{
    i = 1;
    var t = new Type2();
    Type2.Foo();
}".WrapInCSharpClass("Type1");
            var fixtest2 = @"public static void Foo() { }".WrapInCSharpClass("Type2");
            await VerifyCSharpFixAllAsync(new[] { source1, source2 }, new[] { fixtest1, fixtest2 });
        }

        [Fact]
        public async Task MakeMethodStaticWithReferenceInDifferentDocsWithCallsOnTheSameLine()
        {
            var source1 = @"private int i;
void Bar()
{
    i = 1;
    var t = new LargeTypeName();
    var result = t.Foo() + t.Foo();
}".WrapInCSharpClass("Type1");
            var source2 = @"public int Foo() { return 1; }".WrapInCSharpClass("LargeTypeName");
            var fixtest1 = @"private int i;
void Bar()
{
    i = 1;
    var t = new LargeTypeName();
    var result = LargeTypeName.Foo() + LargeTypeName.Foo();
}".WrapInCSharpClass("Type1");
            var fixtest2 = @"public static int Foo() { return 1; }".WrapInCSharpClass("LargeTypeName");
            await VerifyCSharpFixAllAsync(new[] { source1, source2 }, new[] { fixtest1, fixtest2 });
        }

        [Fact]
        public async Task MakeMethodStaticWhenReferencingAsAMethodGroup()
        {
            const string source = @"
    using System;
    namespace ConsoleApplication1
    {
        class TypeName
        {
            public virtual void Foo()
            {
                Func<int> i = Bar;
            }
            public int Bar() => 1;
        }
        class Context
        {
            private int i;
            public void Register(Func<int> f) { i++; }
        }
    }";
            const string fixtest = @"
    using System;
    namespace ConsoleApplication1
    {
        class TypeName
        {
            public virtual void Foo()
            {
                Func<int> i = Bar;
            }
            public static int Bar() => 1;
        }
        class Context
        {
            private int i;
            public void Register(Func<int> f) { i++; }
        }
    }";
            await VerifyCSharpFixAsync(source, fixtest);
        }

        [Fact]
        public async Task MakeMethodStaticWhenReferencingAsAMethodGroupPassedToAFunction()
        {
            const string source = @"
    using System;
    namespace ConsoleApplication1
    {
        class TypeName
        {
            public virtual void Foo(Context c) => c.Register(Bar);
            public int Bar() => 1;
        }
        class Context
        {
            private int i;
            public void Register(Func<int> f) { i++; }
        }
    }";
            const string fixtest = @"
    using System;
    namespace ConsoleApplication1
    {
        class TypeName
        {
            public virtual void Foo(Context c) => c.Register(Bar);
            public static int Bar() => 1;
        }
        class Context
        {
            private int i;
            public void Register(Func<int> f) { i++; }
        }
    }";
            await VerifyCSharpFixAsync(source, fixtest);
        }

        [Fact]
        public async Task NoDiagnosticWhenImplementingInterface()
        {
            const string source = @"
    using System;
    namespace ConsoleApplication1
    {
        interface ITypeName
        {
            int Bar();
        }
        class TypeName : ITypeName
        {
            public int Bar() => 1;
        }
    }";
            await VerifyCSharpHasNoDiagnosticsAsync(source);
        }
    }
}