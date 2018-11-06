using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace MutationCreator
{
    public class DataFlow
    {
        MethodDeclarationSyntax method;
        ControlFlowGraph cfg;
        ISet<SyntaxNode> visitedNodes;
        bool reachingRunAgain = true;
        bool liveVarRunAgain = true;

        Dictionary<SyntaxNode, InOutData<SyntaxNode>> reachingDefDictionary = new Dictionary<SyntaxNode, InOutData<SyntaxNode>>();
        Dictionary<SyntaxNode, InOutData<IdentifierNameSyntax>> liveVarDictionary = new Dictionary<SyntaxNode, InOutData<IdentifierNameSyntax>>();

        public DataFlow(MethodDeclarationSyntax method, ControlFlowGraph cfg)
        {
            this.method = method;
            this.cfg = cfg;
            while (reachingRunAgain)
            {
                reachingRunAgain = false;
                visitedNodes = new HashSet<SyntaxNode>();
                constructReachingDef(cfg.GetEntryPoint());
            }
            while (liveVarRunAgain)
            {
                liveVarRunAgain = false;
                foreach(SyntaxNode leafNode in cfg.GetLeafNodes())
                {
                    visitedNodes = new HashSet<SyntaxNode>();
                    constructLiveVar(leafNode);
                }                
            }            
        }

        public bool containsReachingDef(IdentifierNameSyntax identifierName)
        {
            return reachingDefDictionary.ContainsKey(identifierName);
        }

        public static ISet<T> copySet<T>(ISet<T> original) where T: SyntaxNode
        {
            ISet<T> toReturn = new HashSet<T>();
            foreach(T node in original)
            {
                toReturn.Add(node);
            }
            return toReturn;
        }

        public String ReachingToString()
        {
            StringBuilder toReturn = new StringBuilder();
            foreach(SyntaxNode node in reachingDefDictionary.Keys)
            {
                toReturn.AppendLine(node.ToString());
                toReturn.AppendLine(reachingDefDictionary[node].ToString());
            }
            return toReturn.ToString();
        }

        public String LiveVarToString()
        {
            StringBuilder toReturn = new StringBuilder();
            foreach (SyntaxNode node in liveVarDictionary.Keys)
            {
                toReturn.AppendLine(node.ToString());
                toReturn.AppendLine(liveVarDictionary[node].ToStringWithParents());
            }
            return toReturn.ToString();
        }

        private void liveVarHelper(SyntaxNode node, ISet<IdentifierNameSyntax> OutWithRemovedNodesRemoved)
        {
            CustomSyntaxVisitor visitor = new CustomSyntaxVisitor();
            visitor.Visit(node);
            if (visitor.isFor || visitor.isWhile || visitor.isIf)
            {
                //do nothing
            }
            else if (visitor.isAss)
            {
                //only dive into right side
                AssignmentExpressionSyntax assignmentExpression = node as AssignmentExpressionSyntax;
                ISet<IdentifierNameSyntax> uses = FindIdentifierNameSyntax(assignmentExpression.Right);
                foreach (IdentifierNameSyntax lineUse in uses)
                {
                    OutWithRemovedNodesRemoved.Add(lineUse);
                }           
            }
            else if (visitor.isLocalDec)
            {
                LocalDeclarationStatementSyntax localDeclarationStatement = node as LocalDeclarationStatementSyntax;
                ISet<IdentifierNameSyntax> uses = FindIdentifierNameSyntax(localDeclarationStatement.ChildNodes().First().ChildNodes().ElementAtOrDefault(1).ChildNodes().First());
                foreach (IdentifierNameSyntax lineUse in uses)
                {
                    OutWithRemovedNodesRemoved.Add(lineUse);
                }
            }
            else if (visitor.isVarDec)
            {
                VariableDeclarationSyntax variableDeclaration = node as VariableDeclarationSyntax;
                ISet<IdentifierNameSyntax> uses = FindIdentifierNameSyntax(variableDeclaration.ChildNodes().ElementAtOrDefault(1).ChildNodes().First());
                foreach (IdentifierNameSyntax lineUse in uses)
                {
                    OutWithRemovedNodesRemoved.Add(lineUse);
                }
            }
            else
            {
                //Dive into whole statement
                ISet<IdentifierNameSyntax> uses = FindIdentifierNameSyntax(node);
                if (uses.Count != 0)
                {
                    foreach (IdentifierNameSyntax lineUse in uses)
                    {
                        OutWithRemovedNodesRemoved.Add(lineUse);
                    }
                }
            }
        }

        private void constructLiveVar(SyntaxNode node)
        {                                     
            if (node == null)
            {
                return;
            }

            if (!liveVarDictionary.ContainsKey(node))
            {
                liveVarDictionary[node] = new InOutData<IdentifierNameSyntax>();
                liveVarRunAgain = true;
            }

            //Set up out set
            ISet<SyntaxNode> subsequentNodes = cfg.GetSuccessors(node);
            if (subsequentNodes.Count != 0)
            {
                foreach (SyntaxNode subsequentNode in subsequentNodes)
                {
                    if (liveVarDictionary.ContainsKey(subsequentNode))
                    {
                        if (!liveVarDictionary[subsequentNode].inSet.IsSubsetOf(liveVarDictionary[node].outSet))
                        {
                            liveVarDictionary[node].outSet.UnionWith(liveVarDictionary[subsequentNode].inSet);
                            liveVarRunAgain = true;
                        }
                    }
                }
            }

            //Set up in set
            AssignmentExpressionSyntax assignmentExpressionSyntax = node.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>().FirstOrDefault();
            LocalDeclarationStatementSyntax localDeclarationStatementSyntax = node as LocalDeclarationStatementSyntax;
            VariableDeclarationSyntax variableDeclarationSyntax = node as VariableDeclarationSyntax;
            if (assignmentExpressionSyntax != null)
            {
                ISet<IdentifierNameSyntax> toRemove = new HashSet<IdentifierNameSyntax>();
                foreach (IdentifierNameSyntax identifierNameSyntax in liveVarDictionary[node].outSet)
                {
                    if (getAssignmentOrLocalVarName(assignmentExpressionSyntax).Equals(identifierNameSyntax.ToString()))
                    {
                        toRemove.Add(identifierNameSyntax);
                    }
                }
                ISet<IdentifierNameSyntax> OutWithRemovedNodesRemoved = copySet(liveVarDictionary[node].outSet);
                OutWithRemovedNodesRemoved.ExceptWith(toRemove);

                liveVarHelper(node, OutWithRemovedNodesRemoved);

                if (!OutWithRemovedNodesRemoved.SetEquals(liveVarDictionary[node].inSet) && !OutWithRemovedNodesRemoved.SetEquals(liveVarDictionary[node].outSet))
                {
                    liveVarDictionary[node].inSet = OutWithRemovedNodesRemoved;
                    liveVarRunAgain = true;
                }
            }
            else if(localDeclarationStatementSyntax != null)
            {
                ISet<IdentifierNameSyntax> toRemove = new HashSet<IdentifierNameSyntax>();
                foreach (IdentifierNameSyntax identifierNameSyntax in liveVarDictionary[node].outSet)
                {
                    if (getAssignmentOrLocalVarName(localDeclarationStatementSyntax).Equals(identifierNameSyntax.ToString()))
                    {
                        toRemove.Add(identifierNameSyntax);
                    }
                }
                ISet<IdentifierNameSyntax> OutWithRemovedNodesRemoved = copySet(liveVarDictionary[node].outSet);
                OutWithRemovedNodesRemoved.ExceptWith(toRemove);

                liveVarHelper(node, OutWithRemovedNodesRemoved);

                if (!OutWithRemovedNodesRemoved.SetEquals(liveVarDictionary[node].inSet) && !OutWithRemovedNodesRemoved.SetEquals(liveVarDictionary[node].outSet))
                {
                    liveVarDictionary[node].inSet = OutWithRemovedNodesRemoved;
                    liveVarRunAgain = true;
                }
            }
            else if(variableDeclarationSyntax != null)
            {
                ISet<IdentifierNameSyntax> toRemove = new HashSet<IdentifierNameSyntax>();
                foreach (IdentifierNameSyntax identifierNameSyntax in liveVarDictionary[node].outSet)
                {
                    if (getAssignmentOrLocalVarName(variableDeclarationSyntax).Equals(identifierNameSyntax.ToString()))
                    {
                        if (liveVarDictionary[node].inSet.Contains(identifierNameSyntax))
                        {
                            toRemove.Add(identifierNameSyntax);
                        }
                    }
                }
                ISet<IdentifierNameSyntax> OutWithRemovedNodesRemoved = copySet(liveVarDictionary[node].outSet);
                OutWithRemovedNodesRemoved.ExceptWith(toRemove);

                liveVarHelper(node, OutWithRemovedNodesRemoved);

                if (!OutWithRemovedNodesRemoved.SetEquals(liveVarDictionary[node].inSet) && !OutWithRemovedNodesRemoved.SetEquals(liveVarDictionary[node].outSet))
                {
                    liveVarDictionary[node].inSet = OutWithRemovedNodesRemoved;
                    liveVarRunAgain = true;
                }
            }
            else
            {
                ISet<IdentifierNameSyntax> outCopy = copySet(liveVarDictionary[node].outSet);

                liveVarHelper(node, outCopy);

                if (!outCopy.SetEquals(liveVarDictionary[node].inSet))
                {
                    liveVarDictionary[node].inSet = outCopy;
                    liveVarRunAgain = true;
                }
            }

            visitedNodes.Add(node);

            foreach (SyntaxNode predecessor in cfg.GetPredecessors(node))
            {
                if (!visitedNodes.Contains(predecessor))
                {
                    constructLiveVar(predecessor);
                }
            }
        }

        private void constructReachingDef(SyntaxNode node)
        {
            if(node == null)
            {
                return;
            }           

            if (!reachingDefDictionary.ContainsKey(node))
            {
                reachingDefDictionary[node] = new InOutData<SyntaxNode>();
                reachingRunAgain = true;
            }

            ISet<SyntaxNode> previousNodes = cfg.GetPredecessors(node);
            if(previousNodes.Count != 0)
            {
                foreach(SyntaxNode previousNode in previousNodes)
                {
                    if (reachingDefDictionary.ContainsKey(previousNode))
                    {
                        if (!reachingDefDictionary[previousNode].outSet.IsSubsetOf(reachingDefDictionary[node].inSet))
                        {
                            reachingDefDictionary[node].inSet.UnionWith(reachingDefDictionary[previousNode].outSet);
                            reachingRunAgain = true;
                        }                        
                    }
                }
            }

            AssignmentExpressionSyntax assignmentExpressionSyntax = node.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>().FirstOrDefault();
            LocalDeclarationStatementSyntax localDeclarationStatementSyntax = node as LocalDeclarationStatementSyntax;
            VariableDeclarationSyntax variableDeclarationSyntax = node as VariableDeclarationSyntax;
            if (assignmentExpressionSyntax != null)
            {
                ISet<SyntaxNode> toRemove = new HashSet<SyntaxNode>();
                foreach (SyntaxNode inDef in reachingDefDictionary[node].inSet)
                {
                    String toTest = getAssignmentOrLocalVarName(inDef);
                    if (toTest != null && toTest.Equals(getAssignmentOrLocalVarName(assignmentExpressionSyntax)))
                    {
                        toRemove.Add(inDef);
                    }
                }
                ISet<SyntaxNode> InWithRemovedNodesRemoved = copySet(reachingDefDictionary[node].inSet);
                InWithRemovedNodesRemoved.ExceptWith(toRemove);
                InWithRemovedNodesRemoved.Add(assignmentExpressionSyntax);

                if (!InWithRemovedNodesRemoved.SetEquals(reachingDefDictionary[node].inSet) && !InWithRemovedNodesRemoved.SetEquals(reachingDefDictionary[node].outSet))
                {
                    reachingDefDictionary[node].outSet = InWithRemovedNodesRemoved;
                    reachingRunAgain = true;
                }
            }
            else if (localDeclarationStatementSyntax != null)
            {
                ISet<SyntaxNode> toRemove = new HashSet<SyntaxNode>();
                foreach (SyntaxNode inDef in reachingDefDictionary[node].inSet)
                {
                    String toTest = getAssignmentOrLocalVarName(inDef);
                    if (toTest != null && toTest.Equals(getAssignmentOrLocalVarName(localDeclarationStatementSyntax)))
                    {
                        toRemove.Add(inDef);
                    }
                }
                ISet<SyntaxNode> InWithRemovedNodesRemoved = copySet(reachingDefDictionary[node].inSet);
                InWithRemovedNodesRemoved.ExceptWith(toRemove);
                InWithRemovedNodesRemoved.Add(localDeclarationStatementSyntax);

                if (!InWithRemovedNodesRemoved.SetEquals(reachingDefDictionary[node].inSet) && !InWithRemovedNodesRemoved.SetEquals(reachingDefDictionary[node].outSet))
                {
                    reachingDefDictionary[node].outSet = InWithRemovedNodesRemoved;
                    reachingRunAgain = true;
                }
            }
            else if(variableDeclarationSyntax != null)
            {
                ISet<SyntaxNode> toRemove = new HashSet<SyntaxNode>();
                foreach (SyntaxNode inDef in reachingDefDictionary[node].inSet)
                {
                    String toTest = getAssignmentOrLocalVarName(inDef);
                    if (toTest != null && toTest.Equals(getAssignmentOrLocalVarName(variableDeclarationSyntax)))
                    {
                        toRemove.Add(inDef);
                    }
                }
                ISet<SyntaxNode> InWithRemovedNodesRemoved = copySet(reachingDefDictionary[node].inSet);
                InWithRemovedNodesRemoved.ExceptWith(toRemove);
                InWithRemovedNodesRemoved.Add(variableDeclarationSyntax);

                if (!InWithRemovedNodesRemoved.SetEquals(reachingDefDictionary[node].inSet) && !InWithRemovedNodesRemoved.SetEquals(reachingDefDictionary[node].outSet))
                {
                    reachingDefDictionary[node].outSet = InWithRemovedNodesRemoved;
                    reachingRunAgain = true;
                }
            }
            else
            {
                if (!reachingDefDictionary[node].outSet.SetEquals(reachingDefDictionary[node].inSet))
                {
                    reachingDefDictionary[node].outSet.UnionWith(reachingDefDictionary[node].inSet);
                    reachingRunAgain = true;
                }
            }

            visitedNodes.Add(node);

            foreach (SyntaxNode successor in cfg.GetSuccessors(node))
            {
                if (!visitedNodes.Contains(successor))
                {
                    constructReachingDef(successor);
                }                
            }
        }

        public static String getAssignmentOrLocalVarName(SyntaxNode node)
        {
            AssignmentExpressionSyntax assignmentExpressionSyntax = node as AssignmentExpressionSyntax;
            if(assignmentExpressionSyntax != null)
            {
                return assignmentExpressionSyntax.Left.ToString();
            }
            LocalDeclarationStatementSyntax localDeclarationStatementSyntax = node as LocalDeclarationStatementSyntax;
            if (localDeclarationStatementSyntax != null)
            {
                VariableDeclaratorSyntax variableDeclaratorSyntax = localDeclarationStatementSyntax.ChildNodes().First().ChildNodes().ElementAtOrDefault(1) as VariableDeclaratorSyntax;
                if(variableDeclaratorSyntax != null)
                {
                    return variableDeclaratorSyntax.Identifier.ToString();
                }
            }
            VariableDeclarationSyntax variableDeclarationSyntax = node as VariableDeclarationSyntax;
            if(variableDeclarationSyntax != null)
            {
                VariableDeclaratorSyntax variableDeclaratorSyntax = variableDeclarationSyntax.ChildNodes().ElementAtOrDefault(1) as VariableDeclaratorSyntax;
                if (variableDeclaratorSyntax != null)
                {
                    return variableDeclaratorSyntax.Identifier.ToString();
                }
            }
            return null;
        }

        public ISet<SyntaxNode> GetReachingDefinitions(IdentifierNameSyntax use)
        {
            return reachingDefDictionary[use].inSet as ISet<SyntaxNode>;
        }

        public ISet<IdentifierNameSyntax> GetLiveVariables(AssignmentExpressionSyntax def)
        {
            return liveVarDictionary[def].outSet;
        }

        public ISet<IdentifierNameSyntax> GetDefUseChain(AssignmentExpressionSyntax def)
        {
            throw new NotImplementedException();
        }

        private static ISet<IdentifierNameSyntax> FindIdentifierNameSyntax(SyntaxNode node)
        {
            ISet<IdentifierNameSyntax> toReturn = new HashSet<IdentifierNameSyntax>();
            foreach(SyntaxNode descendant in node.DescendantNodesAndSelf())
            {
                IdentifierNameSyntax toAdd = descendant as IdentifierNameSyntax;
                if(toAdd != null)
                {
                    toReturn.Add(toAdd);
                }
            }
            return toReturn;
        }
    }

    internal class InOutData<T> where T : SyntaxNode
    {
        public ISet<T> inSet;
        public ISet<T> outSet;

        public InOutData()
        {
            inSet = new HashSet<T>();
            outSet = new HashSet<T>();
        }

        public override String ToString()
        {
            StringBuilder toReturn = new StringBuilder();
            toReturn.AppendLine("IN:");
            foreach(T item in inSet)
            {
                toReturn.AppendLine(item.ToString());
            }
            toReturn.AppendLine("OUT:");
            foreach(T item in outSet)
            {
                toReturn.AppendLine(item.ToString());
            }
            return toReturn.ToString();
        }

        public String ToStringWithParents()
        {
            StringBuilder toReturn = new StringBuilder();
            toReturn.AppendLine("IN:");
            foreach (T item in inSet)
            {
                toReturn.AppendLine(item.ToString() + ": " + item.Parent.ToString());
            }
            toReturn.AppendLine("OUT:");
            foreach (T item in outSet)
            {
                toReturn.AppendLine(item.ToString() + ": " + item.Parent.ToString());
            }
            return toReturn.ToString();
        }

    }

    internal class CustomSyntaxVisitor : CSharpSyntaxVisitor
    {
        public bool isIf = false;
        public bool isWhile = false;
        public bool isFor = false;
        public bool isAss = false;
        public bool isVarDec = false;
        public bool isLocalDec = false;

        public void reset()
        {
            isIf = false;
            isWhile = false;
            isFor = false;
            isAss = false;
            isVarDec = false;
            isLocalDec = false;
        }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            reset();
            isIf = true;
            base.VisitIfStatement(node);
        }

        public override void VisitWhileStatement(WhileStatementSyntax node)
        {
            reset();
            isWhile = true;
            base.VisitWhileStatement(node);
        }

        public override void VisitForStatement(ForStatementSyntax node)
        {
            reset();
            isFor = true;
            base.VisitForStatement(node);
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            reset();
            isAss = true;
            base.VisitAssignmentExpression(node);
        }

        public override void VisitVariableDeclaration(VariableDeclarationSyntax node)
        {
            reset();
            isVarDec = true;
            base.VisitVariableDeclaration(node);
        }

        public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            reset();
            isLocalDec = true;
            base.VisitLocalDeclarationStatement(node);
        }
    }
}
