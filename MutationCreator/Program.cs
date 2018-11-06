using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MutationCreator
{
    class Program
    {
        static ISet<SyntaxKind> binaryOperators = new HashSet<SyntaxKind>();
        static ISet<SyntaxKind> unaryOperators = new HashSet<SyntaxKind>();
        static SyntaxTriviaList syntaxTrivias = new SyntaxTriviaList();

        static void Main(string[] args)
        {
            if(args.Length != 2)
            {
                Console.WriteLine("Please include a C# filepath you would like to make mutations to and a directory to put mutations");
                return;
            }
            if (!args[0].EndsWith(".cs"))
            {
                Console.WriteLine("Please include a single C# file with file extension .cs");
                return;
            }

            StreamReader toMutate = getStreamReader(args[0]);
            if(toMutate == null)
            {
                return;
            }

            SyntaxTree tree = CSharpSyntaxTree.ParseText(GetFileText(toMutate));
            
            Compilation compilation = CompileCode(tree);            
            
            if(compilation == null) { 
                Console.WriteLine("Unable to compile tree:" + tree.ToString());
                return;
            }

            setupFields(); //Prepopulates lists of valid mutations

            ISet<SyntaxTree> mutations = makeMutations(tree);

            removeNonCompilableCode(mutations);

            int index = 0;
            string folder = args[1] + @"\Output-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch(Exception e)
            {
                Console.WriteLine("Output path invalid");
                Console.WriteLine(e.ToString());
                return;
            }
            foreach (SyntaxTree mutatedTree in mutations)
            {
                Console.WriteLine(mutatedTree.ToString());
                Console.WriteLine("\n\n\n\n\n");
                File.WriteAllText(folder + @"\Mutant" + index + ".cs", mutatedTree.ToString());
                index++;
            }
        }

        static ISet<SyntaxTree> makeMutations(SyntaxTree ogTree)
        {            
            return traverseTreeForMutations(ogTree.GetRoot(), ogTree.GetRoot(), null);
        }

        static ISet<SyntaxTree> traverseTreeForMutations(SyntaxNode node, SyntaxNode rootNode, DataFlow dataFlow)
        {
            ISet<SyntaxTree> mutationsForCurrentNode;

            MethodDeclarationSyntax methodDeclaration = node as MethodDeclarationSyntax;
            if(methodDeclaration != null)
            {
                ControlFlowGraph CFG = new ControlFlowGraph(methodDeclaration);
                dataFlow = new DataFlow(methodDeclaration, CFG);
            }
            mutationsForCurrentNode = getMutationsForNode(node, rootNode, dataFlow);

            foreach (SyntaxNode descendant in node.ChildNodes())
            {
                mutationsForCurrentNode.UnionWith(traverseTreeForMutations(descendant, rootNode, dataFlow));
            }
            return mutationsForCurrentNode;
        }

        static ISet<SyntaxTree> getMutationsForNode(SyntaxNode node, SyntaxNode rootNode, DataFlow optionalDataFlow = null)
        {
            ISet<SyntaxTree> toReturn = new HashSet<SyntaxTree>();

            BinaryExpressionSyntax binaryExpression = node as BinaryExpressionSyntax;
            PostfixUnaryExpressionSyntax postfixUnaryExpression = node as PostfixUnaryExpressionSyntax;
            PrefixUnaryExpressionSyntax prefixUnaryExpression = node as PrefixUnaryExpressionSyntax;
            BlockSyntax block = node as BlockSyntax;
            StatementSyntax statement = node as StatementSyntax;
            IdentifierNameSyntax identifierName = node as IdentifierNameSyntax;            
            if(binaryExpression != null)
            {
                ISet<SyntaxToken> validMutations = validBinaryOperatorMutations(binaryExpression);
                foreach(SyntaxToken mutatedToken in validMutations)
                {
                    SyntaxNode newRoot = rootNode.ReplaceNode(node, binaryExpression.WithOperatorToken(mutatedToken).WithTrailingTrivia(syntaxTrivias));
                    toReturn.Add(newRoot.SyntaxTree);              
                }
            }
            else if(postfixUnaryExpression != null)
            {
                ISet<SyntaxToken> validMutations = validUnaryOperatorMutations(postfixUnaryExpression);
                foreach(SyntaxToken mutatedToken in validMutations)
                {
                    SyntaxNode newRoot = rootNode.ReplaceNode(node, postfixUnaryExpression.WithOperatorToken(mutatedToken).WithTrailingTrivia(syntaxTrivias));
                    toReturn.Add(newRoot.SyntaxTree);
                }
            }
            else if(prefixUnaryExpression != null)
            {
                ISet<SyntaxToken> validMutations = validUnaryOperatorMutations(prefixUnaryExpression);
                foreach (SyntaxToken mutatedToken in validMutations)
                {
                    SyntaxNode newRoot = rootNode.ReplaceNode(node, prefixUnaryExpression.WithOperatorToken(mutatedToken).WithTrailingTrivia(syntaxTrivias));
                    toReturn.Add(newRoot.SyntaxTree);
                }
            }
            else if(statement != null && block == null)
            {
                //replace statements with semicolons
                toReturn.Add(rootNode.ReplaceNode(node, SyntaxFactory.EmptyStatement(SyntaxFactory.Token(SyntaxKind.SemicolonToken)).WithTrailingTrivia(syntaxTrivias)).SyntaxTree);
            }
            else if(identifierName != null && optionalDataFlow != null)
            {
                //Go through reaching definitions and replace with all other variables available
                ISet<SyntaxToken> validMutations = validIdentifierNames(identifierName, optionalDataFlow);
                foreach(SyntaxToken mutatedToken in validMutations)
                {
                    SyntaxNode newRoot = rootNode.ReplaceNode(node, identifierName.WithIdentifier(mutatedToken).WithTrailingTrivia(syntaxTrivias));
                    toReturn.Add(newRoot.SyntaxTree);
                }
            }

            return toReturn;
        }

        static ISet<SyntaxToken> validIdentifierNames(IdentifierNameSyntax identifierName, DataFlow dataFlow)
        {
            ISet<SyntaxToken> toReturn = new HashSet<SyntaxToken>();
            ISet<SyntaxNode> defs = new HashSet<SyntaxNode>();

            //Not totally satisfied with my method here. It feels jank, but due to the way I did the CFG and data flow, it is what works.
            if(dataFlow.containsReachingDef(identifierName))
            {
                defs = dataFlow.GetReachingDefinitions(identifierName);
            }
            else if (dataFlow.containsReachingDef(identifierName.Parent))
            {
                defs = dataFlow.GetReachingDefinitions(identifierName.Parent);
            }
            else if (dataFlow.containsReachingDef(identifierName.Parent.Parent))
            {
                defs = dataFlow.GetReachingDefinitions(identifierName.Parent.Parent);                
            }

            foreach (SyntaxNode def in defs)
            {
                String varName = DataFlow.getAssignmentOrLocalVarName(def);
                if (!varName.Equals(identifierName.Identifier.Text))
                {
                    toReturn.Add(SyntaxFactory.Identifier(varName));
                }
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
            var Mscorlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            var linq = MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location);
            var compilation = CSharpCompilation.Create("MyCompilation",
                syntaxTrees: new[] { tree }, references: new[] { Mscorlib, linq });

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
            syntaxTrivias = new SyntaxTriviaList();
            syntaxTrivias.Add(SyntaxFactory.SyntaxTrivia(SyntaxKind.SingleLineCommentTrivia, "Mutation"));
            syntaxTrivias.Add(SyntaxFactory.EndOfLine("eof"));
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
