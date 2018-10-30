using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MutationCreator
{
    class Program
    {      
        static void Main(string[] args)
        {
            if(args.Length < 1)
            {
                Console.WriteLine("Please include a C# filepath you would like to make mutations to and a directory to put mutations");
                return;
            }
            StreamReader toMutate = getStreamReader(args[0]);
            if(toMutate == null)
            {
                return;
            }
            SyntaxTree tree = CSharpSyntaxTree.ParseText(GetFileText(toMutate));
            Compilation compilation;
            try
            {
                compilation = CompileCode(tree);
            }
            catch(Exception failedToCompile)
            {
                Console.WriteLine("Unable to compile tree:" + tree.ToString());
                Console.WriteLine(failedToCompile.ToString());
                return;
            }
            SemanticModel model = compilation.GetSemanticModel(compilation.SyntaxTrees.First());
            ISet<SyntaxTree> mutations = makeMutations(tree, model);
        }

        static ISet<SyntaxTree> makeMutations(SyntaxTree ogTree, SemanticModel model)
        {            
            return traverseTreeForMutations(ogTree.GetRoot());
        }

        static ISet<SyntaxTree> traverseTreeForMutations(SyntaxNode node)
        {
            ISet<SyntaxTree> mutationsForCurrentNode = getMutationsForNode(node);

            foreach (SyntaxNode descendant in node.DescendantNodes())
            {
                mutationsForCurrentNode.UnionWith(traverseTreeForMutations(descendant));
            }
            return mutationsForCurrentNode;
        }

        static ISet<SyntaxTree> getMutationsForNode(SyntaxNode node)
        {
            ISet<SyntaxTree> toReturn = new HashSet<SyntaxTree>();

            BinaryExpressionSyntax binaryExpression = node as BinaryExpressionSyntax;
            if(binaryExpression != null)
            {
                ISet<SyntaxToken> validMutations = validOperatorMutations(binaryExpression);
            }
            int x = 1 + 5;

            return toReturn;
        }

        static ISet<SyntaxToken> validOperatorMutations(BinaryExpressionSyntax binaryExpression)
        {
            ISet<SyntaxToken> toReturn = new HashSet<SyntaxToken>();
            SyntaxToken nodeOperator = binaryExpression.OperatorToken;
            SyntaxKind kind = nodeOperator.Kind();
            if (kind.Equals(SyntaxKind.PlusToken)) {
                SyntaxFactory.Token(SyntaxKind.MinusToken);
            }
        }

        static StreamReader getStreamReader(String filepath)
        {
            StreamReader toMutate;
            try
            {
                toMutate = new StreamReader(filepath);
            }
            catch (Exception unableToOpenFile)
            {
                Console.WriteLine("Unable to open file: " + filepath);
                Console.WriteLine(unableToOpenFile.ToString());
                return null;
            }
            return toMutate;
        }

        static String GetFileText(StreamReader toMutate)
        {
            StringBuilder fileContents = new StringBuilder();
            string line;
            while (true)
            {
                try
                {
                    line = toMutate.ReadLine();
                }
                catch (Exception unableToReadLine)
                {
                    Console.WriteLine("Failed to read line");
                    Console.WriteLine(unableToReadLine.ToString());
                    break;
                }
                if (line == null)
                {
                    break;
                }
                fileContents.AppendLine(line);
            }
            return fileContents.ToString();
        }

        static CSharpCompilation CompileCode(SyntaxTree tree)
        {
            Console.WriteLine("=====COMPILATION=====");
            // Q: What does CSharpCompilation.Create do?
            var Mscorlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            var linq = MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location);
            var compilation = CSharpCompilation.Create("MyCompilation",
                syntaxTrees: new[] { tree }, references: new[] { Mscorlib, linq });

            // Print all the ways this snippet is horribly coded.
            // FIXME: how do we fix the Linq error?
            //Console.WriteLine("Compile errors: ");
            //Console.WriteLine(compilation.GetDiagnostics().Select(s => s.ToString()).Aggregate((s, s2) => s.ToString() + " " + s2.ToString()));
            //Console.WriteLine();

            // FIXME: how do I abort on only the most serious errors?
            if (compilation.GetDiagnostics().Where(msg => msg.Severity == DiagnosticSeverity.Error).Count() > 0)
            {
                throw new Exception("Compilation failed.");
            }

            return compilation;
        }
    }
}
