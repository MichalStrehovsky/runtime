// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

using ILCompiler.DependencyAnalysisFramework;

using Internal.Text;

namespace ILCompiler.DependencyAnalysis
{
    /// <summary>
    /// UnboxingStubMap - WASM-only table that maps unboxing stubs to their targets. The layout is:
    /// uint simpleCount, uint instantiatingCount, followed by simpleCount pairs of (stub, target)
    /// function table indices for simple unboxing stubs and instantiatingCount pairs of (stub, target)
    /// function table indices for unboxing and instantiating stubs.
    /// </summary>
    public sealed class UnboxingStubMapNode : ObjectNode, ISymbolDefinitionNode
    {
        public override bool IsShareable => false;

        public override ObjectNodeSection GetSection(NodeFactory factory) => ObjectNodeSection.ReadOnlyDataSection;

        public override bool StaticDependenciesAreComputed => true;

        public int Offset => 0;

        protected internal override int Phase => (int)ObjectNodePhase.Ordered;
        public override int ClassCode => (int)ObjectNodeOrder.UnboxingStubMapNode;

        public void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append(nameMangler.CompilationUnitPrefix).Append("_unboxing_stub_map"u8);
        }

        protected override string GetName(NodeFactory factory) => this.GetMangledName(factory.NameMangler);

        public override ObjectData GetData(NodeFactory factory, bool relocsOnly = false)
        {
            // The data emission relies on the set of compiled methods which is incomplete during
            // dependency tracking. This node doesn't keep anything alive - it only describes stubs
            // that were generated for other reasons.
            if (relocsOnly)
            {
                return new ObjectData(Array.Empty<byte>(), Array.Empty<Relocation>(), 1, new ISymbolDefinitionNode[] { this });
            }

            var simplePairs = new List<(ISymbolNode Stub, ISymbolNode Target)>();
            foreach (UnboxingStubNode stub in factory.MetadataManager.GetUnboxingStubs())
            {
                AddPairIfMarked(simplePairs, stub, factory.MethodEntrypoint(stub.Method));
            }

            var instantiatingPairs = new List<(ISymbolNode Stub, ISymbolNode Target)>();
            foreach (IMethodBodyNode body in factory.MetadataManager.GetCompiledMethodBodies())
            {
                if (body is ISpecialUnboxThunkNode { IsSpecialUnboxingThunk: true } thunk)
                {
                    AddPairIfMarked(instantiatingPairs, thunk, thunk.GetUnboxingThunkTarget(factory));
                }
            }

            ObjectDataBuilder objData = new ObjectDataBuilder(factory, relocsOnly);
            objData.RequireInitialPointerAlignment();
            objData.AddSymbol(this);

            objData.EmitInt(simplePairs.Count);
            objData.EmitInt(instantiatingPairs.Count);

            foreach ((ISymbolNode stub, ISymbolNode target) in simplePairs)
            {
                objData.EmitReloc(stub, RelocType.WASM_TABLE_INDEX_I32);
                objData.EmitReloc(target, RelocType.WASM_TABLE_INDEX_I32);
            }

            foreach ((ISymbolNode stub, ISymbolNode target) in instantiatingPairs)
            {
                objData.EmitReloc(stub, RelocType.WASM_TABLE_INDEX_I32);
                objData.EmitReloc(target, RelocType.WASM_TABLE_INDEX_I32);
            }

            return objData.ToObjectData();
        }

        private static void AddPairIfMarked(List<(ISymbolNode Stub, ISymbolNode Target)> pairs, ISymbolNode stub, ISymbolNode target)
        {
            // Only describe stubs whose targets were generated as standalone bodies. The target could
            // have been inlined into the stub, in which case there's nothing to point at.
            if (stub is DependencyNodeCore<NodeFactory> { Marked: true }
                && target is DependencyNodeCore<NodeFactory> { Marked: true })
            {
                pairs.Add((stub, target));
            }
        }
    }
}
