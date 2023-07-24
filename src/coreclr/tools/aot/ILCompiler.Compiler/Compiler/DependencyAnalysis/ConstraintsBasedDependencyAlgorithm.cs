// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

using DependencyList = ILCompiler.DependencyAnalysisFramework.DependencyNodeCore<ILCompiler.DependencyAnalysis.NodeFactory>.DependencyList;

namespace ILCompiler.DependencyAnalysis
{
    internal static class ConstraintsBasedDependencyAlgorithm
    {
        public static void AddDependenciesDueToConstraints(ref DependencyList dependencies, NodeFactory factory, Instantiation instantiation)
        {
            foreach (GenericParameterDesc param in instantiation)
            {
                try
                {
                    foreach (TypeDesc constraintType in param.TypeConstraints)
                        TypeMetadataNode.GetMetadataDependencies(ref dependencies, factory, constraintType, "Type constraint");
                }
                catch (TypeSystemException)
                {
                }
            }
        }
    }
}
