// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

using ILCompiler.DependencyAnalysisFramework;

using Internal.TypeSystem;

using Debug = System.Diagnostics.Debug;

namespace ILCompiler.DependencyAnalysis
{
    internal class InterfaceUseNode : DependencyNodeCore<NodeFactory>
    {
        public DefType Type { get; }

        public InterfaceUseNode(DefType type)
        {
            Debug.Assert(!type.IsRuntimeDeterminedSubtype);
            Type = type;
        }

        protected override string GetName(NodeFactory factory) => throw new NotImplementedException();

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory) => throw new NotImplementedException();

        public override bool InterestingForDynamicDependencyAnalysis => false;
        public override bool HasDynamicDependencies => false;
        public override bool HasConditionalStaticDependencies => false;
        public override bool StaticDependenciesAreComputed => true;
        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory factory) => null;
        public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(List<DependencyNodeCore<NodeFactory>> markedNodes, int firstNode, NodeFactory context) => null;
    }
}
