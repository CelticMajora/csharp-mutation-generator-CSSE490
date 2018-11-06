using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MutationCreator
{
    public class ControlFlowGraph
    {
        SyntaxNode entryNode;
        public Dictionary<SyntaxNode, ISet<SyntaxNode>> graph = new Dictionary<SyntaxNode, ISet<SyntaxNode>>();

        public ControlFlowGraph(MethodDeclarationSyntax method)
        {
            BlockSyntax body = method.Body;
            this.entryNode = body.ChildNodes().First();
            buildCFG(this.entryNode, null, null);
        }

        public override String ToString()
        {
            StringBuilder stringBuilder = new StringBuilder();            
            foreach(SyntaxNode node in graph.Keys)
            {
                stringBuilder.Append(node.ToString());
                stringBuilder.Append(":");
                foreach (SyntaxNode child in graph[node])
                {
                    stringBuilder.AppendLine();
                    stringBuilder.Append(child.ToString());                   
                }
                stringBuilder.AppendLine();
                stringBuilder.AppendLine();
                stringBuilder.AppendLine();
            }
            return stringBuilder.ToString();
        }

        internal ISet<SyntaxNode> GetLeafNodes()
        {
            ISet<SyntaxNode> toReturn = new HashSet<SyntaxNode>();
            foreach(SyntaxNode node in graph.Keys)
            {
                if (graph[node].Count == 0)
                {
                    toReturn.Add(node);
                }
            }
            return toReturn;
        }

        private void buildCFG(SyntaxNode node, SyntaxNode loopJumpBackTarget, SyntaxNode ifJumpForwardTarget)
        {
            if (isIf(node))
            {
                SyntaxNode condition = node;
                SyntaxNode block = nextNode(condition);
                SyntaxNode elseNode = nextNode(block);

                SyntaxNode successor = normalizeBlockedCode(nextNode(node.Parent));

                this.graph[condition] = new HashSet<SyntaxNode>();

                SyntaxNode blockStart = block.ChildNodes().First();
                if(blockStart != null && successor == null)
                {
                    this.graph[condition].Add(blockStart);
                    buildCFG(blockStart, loopJumpBackTarget, ifJumpForwardTarget);
                }
                else if(blockStart != null)
                {
                    this.graph[condition].Add(blockStart);
                    buildCFG(blockStart, loopJumpBackTarget, successor);
                }
                                
                if (elseNode != null)
                {
                    SyntaxNode elseBody = elseNode.ChildNodes().First();
                    if (elseBody != null && successor == null)
                    {
                        this.graph[condition].Add(elseBody);
                        buildCFG(elseBody, loopJumpBackTarget, ifJumpForwardTarget);
                    }
                    else if(elseBody != null)
                    {
                        this.graph[condition].Add(elseBody);
                        buildCFG(elseBody, loopJumpBackTarget, successor);
                    }
                }
            }
            else if (isWhile(node))
            {
                SyntaxNode condition = node;
                SyntaxNode block = nextNode(condition);
                SyntaxNode blockStart = block.ChildNodes().First();
                SyntaxNode successor = normalizeBlockedCode(nextNode(node.Parent));

                this.graph[condition] = new HashSet<SyntaxNode>();
                
                if (blockStart != null)
                {
                    this.graph[condition].Add(blockStart);
                    buildCFG(blockStart, condition, ifJumpForwardTarget);
                }
                
                if (successor != null)
                {
                    this.graph[condition].Add(successor);
                    buildCFG(successor, loopJumpBackTarget, ifJumpForwardTarget);
                }
                else if (loopJumpBackTarget != null)
                {
                    this.graph[condition].Add(loopJumpBackTarget);
                }
            }
            else if (isFor(node))
            {
                SyntaxNode varDeclaration = node;
                SyntaxNode condition = nextNode(varDeclaration);
                SyntaxNode incrementer = nextNode(condition);
                SyntaxNode block = nextNode(incrementer);
                SyntaxNode blockStart = block.ChildNodes().First();
                SyntaxNode successor = normalizeBlockedCode(nextNode(node.Parent));
                
                this.graph[varDeclaration] = new HashSet<SyntaxNode>();
                this.graph[varDeclaration].Add(condition);
                this.graph[incrementer] = new HashSet<SyntaxNode>();
                this.graph[incrementer].Add(condition);
                this.graph[condition] = new HashSet<SyntaxNode>();
                
                if (blockStart != null)
                {
                    this.graph[condition].Add(blockStart);
                    buildCFG(blockStart, incrementer, ifJumpForwardTarget);
                }
                
                if (successor != null)
                {
                    this.graph[condition].Add(successor);
                    buildCFG(successor, loopJumpBackTarget, ifJumpForwardTarget);
                }
                else if(loopJumpBackTarget != null)
                {
                    this.graph[condition].Add(loopJumpBackTarget);
                }
            }
            else if (isReturn(node))
            {
                this.graph[node] = new HashSet<SyntaxNode>();
            }
            else
            {
                SyntaxNode successor = normalizeBlockedCode(nextNode(node));
                this.graph[node] = new HashSet<SyntaxNode>();
                if(successor != null)
                {
                    this.graph[node].Add(successor);
                    buildCFG(successor, loopJumpBackTarget, ifJumpForwardTarget);
                }
                else if(loopJumpBackTarget != null)
                {
                    this.graph[node].Add(loopJumpBackTarget);
                }
                else if(ifJumpForwardTarget != null)
                {
                    this.graph[node].Add(ifJumpForwardTarget);
                    buildCFG(ifJumpForwardTarget, loopJumpBackTarget, null);
                }
            }
        }

        //True next node will get node after it in the syntax tree rather than stepping into if/for/while statements
        private SyntaxNode nextNode(SyntaxNode predecessor)
        {
            SyntaxNode parent = predecessor.Parent;
            bool isNextNodeCorrect = false;
            foreach(SyntaxNode child in parent.ChildNodes())
            {
                if (isNextNodeCorrect)
                {               
                    return child;
                }
                if (child.Equals(predecessor))
                {
                    isNextNodeCorrect = true;
                }
            }
            return null;
        }

        private SyntaxNode normalizeBlockedCode(SyntaxNode node)
        {
            IfStatementSyntax ifStatement = node as IfStatementSyntax;
            if (ifStatement != null)
            {
                return ifStatement.ChildNodes().First();
            }
            ForStatementSyntax forStatement = node as ForStatementSyntax;
            if (forStatement != null)
            {
                return forStatement.ChildNodes().First();
            }
            WhileStatementSyntax whileStatement = node as WhileStatementSyntax;
            if (whileStatement != null)
            {
                return whileStatement.ChildNodes().First();
            }
            return node;
        }


        private bool isFor(SyntaxNode node)
        {
            ForStatementSyntax statement = node.Parent as ForStatementSyntax;
            return statement != null;
        }

        private bool isReturn(SyntaxNode node)
        {
            ReturnStatementSyntax statement = node as ReturnStatementSyntax;
            return statement != null;
        }

        private bool isWhile(SyntaxNode node)
        {
            WhileStatementSyntax statement = node.Parent as WhileStatementSyntax;
            return statement != null;
        }

        private bool isIf(SyntaxNode node)
        {
            IfStatementSyntax statement = node.Parent as IfStatementSyntax;
            return statement != null;
        }

        public SyntaxNode GetEntryPoint()
        {
            return this.entryNode;
        }

        public ISet<SyntaxNode> GetSuccessors(SyntaxNode node)
        {
            if (graph.ContainsKey(node))
            {
                return graph[node];
            }
            return new HashSet<SyntaxNode>();
        }

        public ISet<SyntaxNode> GetPredecessors(SyntaxNode node)
        {
            HashSet<SyntaxNode> toReturn = new HashSet<SyntaxNode>();
            foreach(SyntaxNode parentNode in graph.Keys)
            {
                if (graph[parentNode].Contains(node))
                {
                    toReturn.Add(parentNode);
                }
            }
            return toReturn;
        }
    }
}
