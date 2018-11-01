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
        static ISet<SyntaxKind> binaryOperators = new HashSet<SyntaxKind>();
        static ISet<SyntaxKind> unaryOperators = new HashSet<SyntaxKind>();

        static void Main(string[] args)
        {
            /*if(args.Length < 1)
            {
                Console.WriteLine("Please include a C# filepath you would like to make mutations to and a directory to put mutations");
                return;
            }
            StreamReader toMutate = getStreamReader(args[0]);
            if(toMutate == null)
            {
                return;
            }*/
            //SyntaxTree tree = CSharpSyntaxTree.ParseText(GetFileText(toMutate));
            SyntaxTree tree = CSharpSyntaxTree.ParseText(@"class Program
            {
                static void Main(string[] args)
                {
                    int x = 0;
                    int y = 1;
                    int z = y;
                    if (x == 0)
                    {
                        y++;
                    }
                    else
                    {
                        --y;
                    }
                    x = y - 3;
                }
            }
            ");
            Compilation compilation = CompileCode(tree);
            
            if(compilation == null) { 
                Console.WriteLine("Unable to compile tree:" + tree.ToString());
                return;
            }
            SemanticModel model = compilation.GetSemanticModel(compilation.SyntaxTrees.First());

            setupFields(); //Prepopulates lists of valid mutations

            ISet<SyntaxTree> mutations = makeMutations(tree, model);

            removeNonCompilableCode(mutations);
            
            foreach(SyntaxTree mutatedTree in mutations)
            {
                Console.WriteLine(mutatedTree.ToString());
                Console.WriteLine("\n\n\n\n\n");
            }
        }

        static ISet<SyntaxTree> makeMutations(SyntaxTree ogTree, SemanticModel model)
        {            
            return traverseTreeForMutations(ogTree.GetRoot(), ogTree.GetRoot());
        }

        static ISet<SyntaxTree> traverseTreeForMutations(SyntaxNode node, SyntaxNode rootNode)
        {
            ISet<SyntaxTree> mutationsForCurrentNode = getMutationsForNode(node, rootNode);

            foreach (SyntaxNode descendant in node.ChildNodes())
            {
                mutationsForCurrentNode.UnionWith(traverseTreeForMutations(descendant, rootNode));
            }
            return mutationsForCurrentNode;
        }

        static ISet<SyntaxTree> getMutationsForNode(SyntaxNode node, SyntaxNode rootNode)
        {
            ISet<SyntaxTree> toReturn = new HashSet<SyntaxTree>();

            BinaryExpressionSyntax binaryExpression = node as BinaryExpressionSyntax;
            PostfixUnaryExpressionSyntax postfixUnaryExpression = node as PostfixUnaryExpressionSyntax;
            PrefixUnaryExpressionSyntax prefixUnaryExpression = node as PrefixUnaryExpressionSyntax;
            StatementSyntax statement = node as StatementSyntax;
            if(binaryExpression != null)
            {
                ISet<SyntaxToken> validMutations = validBinaryOperatorMutations(binaryExpression);
                foreach(SyntaxToken mutatedToken in validMutations)
                {
                    SyntaxNode newRoot = rootNode.ReplaceNode(node, binaryExpression.WithOperatorToken(mutatedToken));
                    toReturn.Add(newRoot.SyntaxTree);              
                }
            }
            else if(postfixUnaryExpression != null)
            {
                ISet<SyntaxToken> validMutations = validUnaryOperatorMutations(postfixUnaryExpression);
                foreach(SyntaxToken mutatedToken in validMutations)
                {
                    SyntaxNode newRoot = rootNode.ReplaceNode(node, postfixUnaryExpression.WithOperatorToken(mutatedToken));
                    toReturn.Add(newRoot.SyntaxTree);
                }
            }
            else if(prefixUnaryExpression != null)
            {
                ISet<SyntaxToken> validMutations = validUnaryOperatorMutations(prefixUnaryExpression);
                foreach (SyntaxToken mutatedToken in validMutations)
                {
                    SyntaxNode newRoot = rootNode.ReplaceNode(node, prefixUnaryExpression.WithOperatorToken(mutatedToken));
                    toReturn.Add(newRoot.SyntaxTree);
                }
            }
            else if(statement != null)
            {
                //replace statements with semicolons
                //toReturn.Add(rootNode.ReplaceNode(node, SyntaxFactory.EmptyStatement(SyntaxFactory.Token(SyntaxKind.SemicolonToken))).SyntaxTree);
            }                      

            return toReturn;
        }

        static ISet<SyntaxToken> validBinaryOperatorMutations(BinaryExpressionSyntax binaryExpression)
        {
            ISet<SyntaxToken> toReturn = new HashSet<SyntaxToken>();
            ISet<SyntaxKind> kind = new HashSet<SyntaxKind>();
            kind.Add(binaryExpression.OperatorToken.Kind());
            foreach (SyntaxKind validKind in binaryOperators.Except(kind))
            {
                toReturn.Add(SyntaxFactory.Token(validKind));
            }            
            return toReturn;
        }

        static ISet<SyntaxToken> validUnaryOperatorMutations(PostfixUnaryExpressionSyntax postfixUnaryExpression)
        {
            ISet<SyntaxToken> toReturn = new HashSet<SyntaxToken>();
            ISet<SyntaxKind> kind = new HashSet<SyntaxKind>();
            kind.Add(postfixUnaryExpression.OperatorToken.Kind());
            foreach(SyntaxKind validKind in unaryOperators.Except(kind))
            {
                toReturn.Add(SyntaxFactory.Token(validKind));
            }
            return toReturn;
        }

        static ISet<SyntaxToken> validUnaryOperatorMutations(PrefixUnaryExpressionSyntax prefixUnaryExpression)
        {
            ISet<SyntaxToken> toReturn = new HashSet<SyntaxToken>();
            ISet<SyntaxKind> kind = new HashSet<SyntaxKind>();
            kind.Add(prefixUnaryExpression.OperatorToken.Kind());
            foreach (SyntaxKind validKind in unaryOperators.Except(kind))
            {
                toReturn.Add(SyntaxFactory.Token(validKind));
            }
            return toReturn;
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

        static void removeNonCompilableCode(ISet<SyntaxTree> mutations)
        {
            ISet<SyntaxTree> toRemove = new HashSet<SyntaxTree>();
            foreach(SyntaxTree mutation in mutations)
            {
                if(CompileCode(mutation) == null)
                {
                    toRemove.Add(mutation);
                }
            }
            mutations.ExceptWith(toRemove);
        }

        static CSharpCompilation CompileCode(SyntaxTree tree)
        {
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
                return null;
            }

            return compilation;
        }

        static void setupFields()
        {
            setupBinaryOperators();
            setupUnaryOperators();
        }

        static void setupBinaryOperators()
        {
            binaryOperators.Clear();
            binaryOperators.Add(SyntaxKind.PlusToken);
            binaryOperators.Add(SyntaxKind.MinusToken);
            binaryOperators.Add(SyntaxKind.AsteriskToken);
            binaryOperators.Add(SyntaxKind.SlashToken);
            binaryOperators.Add(SyntaxKind.EqualsEqualsToken);
            binaryOperators.Add(SyntaxKind.ExclamationEqualsToken);
            binaryOperators.Add(SyntaxKind.LessThanToken);
            binaryOperators.Add(SyntaxKind.LessThanEqualsToken);
            binaryOperators.Add(SyntaxKind.GreaterThanToken);
            binaryOperators.Add(SyntaxKind.GreaterThanEqualsToken);
        }

        static void setupUnaryOperators()
        {
            unaryOperators.Clear();
            unaryOperators.Add(SyntaxKind.PlusPlusToken);
            unaryOperators.Add(SyntaxKind.MinusMinusToken);
        }
    }
}
