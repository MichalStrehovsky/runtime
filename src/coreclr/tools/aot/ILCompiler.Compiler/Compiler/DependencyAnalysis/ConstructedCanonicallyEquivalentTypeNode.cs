// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;

using Internal.TypeSystem;

using ILCompiler.DependencyAnalysisFramework;

namespace ILCompiler.DependencyAnalysis
{
    /// <summary>
    /// Captures the information that a type whose canonical form is the type
    /// represented by this node is present in the dependency graph.
    /// </summary>
    internal sealed class ConstructedCanonicallyEquivalentTypeNode : DependencyNodeCore<NodeFactory>
    {
        public TypeDesc Type { get; }

        public ConstructedCanonicallyEquivalentTypeNode(TypeDesc type)
        {
            Debug.Assert(type.IsCanonicalSubtype(CanonicalFormKind.Any));
            Debug.Assert(type.ConvertToCanonForm(CanonicalFormKind.Specific) == type);
            Type = type;
        }

        protected override string GetName(NodeFactory context) => $"Canonically equivalent type: {Type}";

        public override bool InterestingForDynamicDependencyAnalysis => false;

        public override bool HasDynamicDependencies => false;

        public override bool HasConditionalStaticDependencies => false;

        public override bool StaticDependenciesAreComputed => true;

        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory context) => null;
        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory context) => null;
        public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(List<DependencyNodeCore<NodeFactory>> markedNodes, int firstNode, NodeFactory context) => null;
    }
}
