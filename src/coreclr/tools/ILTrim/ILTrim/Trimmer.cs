// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

using ILCompiler.DependencyAnalysisFramework;

using ILCompiler.DependencyAnalysis;
using ILCompiler.Dataflow;
using System.Linq;
using System.Threading.Tasks;

using static ILCompiler.DependencyAnalysisFramework.DependencyNodeCore<ILCompiler.DependencyAnalysis.NodeFactory>;

namespace ILCompiler
{
    public static class Trimmer
    {
        public static void TrimAssembly(
            string inputPath,
            IReadOnlyList<string> additionalTrimPaths,
            string outputDir,
            IReadOnlyList<string> referencePaths,
            TrimmerSettings settings = null)
        {
            var context = new ILTrimTypeSystemContext();
            settings = settings ?? new TrimmerSettings();

            Dictionary<string, string> references = new();
            foreach (var path in additionalTrimPaths.Concat(referencePaths ?? Enumerable.Empty<string>()))
            {
                var simpleName = Path.GetFileNameWithoutExtension(path);
                references.Add(simpleName, path);
            }
            context.ReferenceFilePaths = references;

            // Get an interned EcmaModule. Direct call to EcmaModule.Create creates an assembly out of thin air without
            // registering it anywhere and once we deal with multiple assemblies that refer to each other, that's a problem.
            EcmaModule module = context.GetModuleFromPath(inputPath);

            EcmaModule corelib = context.GetModuleForSimpleName("System.Private.CoreLib");
            context.SetSystemModule(corelib);

            var trimmedAssemblies = new List<string>(additionalTrimPaths.Select(p => Path.GetFileNameWithoutExtension(p)));
            trimmedAssemblies.Add(Path.GetFileNameWithoutExtension(inputPath));
            var factory = new NodeFactory(trimmedAssemblies, settings);

            DependencyAnalyzerBase<NodeFactory> analyzer = settings.LogStrategy switch
            {
                LogStrategy.None => new DependencyAnalyzer<NoLogStrategy<NodeFactory>, NodeFactory>(factory, resultSorter: null),
                LogStrategy.FirstMark => new DependencyAnalyzer<FirstMarkLogStrategy<NodeFactory>, NodeFactory>(factory, resultSorter: null),
                LogStrategy.FullGraph => new DependencyAnalyzer<FullGraphLogStrategy<NodeFactory>, NodeFactory>(factory, resultSorter: null),
                LogStrategy.EventSource => new DependencyAnalyzer<EventSourceLogStrategy<NodeFactory>, NodeFactory>(factory, resultSorter: null),
                _ => throw new ArgumentException("Invalid log strategy")
            };

            analyzer.ComputeDependencyRoutine += ComputeDependencyNodeDependencies;

            if (!settings.LibraryMode)
            {
                MethodDefinitionHandle entrypointToken = (MethodDefinitionHandle)MetadataTokens.Handle(module.PEReader.PEHeaders.CorHeader.EntryPointTokenOrRelativeVirtualAddress);

                analyzer.AddRoot(factory.MethodDefinition(module, entrypointToken), "Entrypoint");

            }
            else
            {
                int rootNumber = 1;
                List<string> methodNames = new List<string>();
                foreach (var methodHandle in module.MetadataReader.MethodDefinitions)
                {
                    var method = module.MetadataReader.GetMethodDefinition(methodHandle);
                    var type = module.MetadataReader.GetTypeDefinition(method.GetDeclaringType());
                    if (!type.IsNested && IsPublic(type.Attributes) && method.Attributes.IsPublic())
                    {
                        analyzer.AddRoot(factory.MethodDefinition(module, methodHandle), $"LibraryMode_{rootNumber++}");
                    }
                }
            }

            // Root Object.Finalize as used so that types overriding it will not get their override removed
            analyzer.AddRoot(factory.VirtualMethodUse(
                (EcmaMethod)context.GetWellKnownType(WellKnownType.Object).GetMethod("Finalize"u8, null)),
                "Finalizer");

            // Process embedded ILLink.Descriptors.xml from reference assemblies (e.g. System.Private.CoreLib).
            // These descriptors root methods that the runtime needs (like Object.Equals, Object.GetHashCode).
            // Trimmed assemblies' descriptors are handled via ManifestResourceNode; this covers non-trimmed references.
            foreach (var refPath in referencePaths ?? Enumerable.Empty<string>())
            {
                var refModule = context.GetModuleFromPath(refPath);
                ProcessEmbeddedDescriptors(refModule, factory, analyzer);
            }

            analyzer.ComputeMarkedNodes();

            var writers = ModuleWriter.CreateWriters(factory, analyzer.MarkedNodeList);
            if (!File.Exists(outputDir))
                Directory.CreateDirectory(outputDir);
            RunForEach(writers, writer =>
            {
                var ext = writer.AssemblyName == "test" ? ".exe" : ".dll";
                string outputPath = Path.Combine(outputDir, writer.AssemblyName + ext);
                using var outputStream = File.OpenWrite(outputPath);
                writer.Save(outputStream);
            });

            if (settings.LogFile != null) {
                using var logStream = File.OpenWrite(settings.LogFile);
                DgmlWriter.WriteDependencyGraphToStream<NodeFactory>(logStream, analyzer, factory);
            }

            void ComputeDependencyNodeDependencies(List<DependencyNodeCore<NodeFactory>> nodesWithPendingDependencyCalculation) =>
                RunForEach(
                    nodesWithPendingDependencyCalculation.Cast<INodeWithDeferredDependencies>(),
                    node => node.ComputeDependencies(factory));

            void RunForEach<T>(IEnumerable<T> inputs, Action<T> action)
            {
#if !SINGLE_THREADED
                if (settings.MaxDegreeOfParallelism == 1)
#endif
                {
                    foreach (var input in inputs)
                        action(input);
                }
#if !SINGLE_THREADED
                else
                {
                    Parallel.ForEach(
                        inputs,
                        new() { MaxDegreeOfParallelism = settings.EffectiveDegreeOfParallelism },
                        action);
                }
#endif
            }
        }

        private static bool IsPublic(TypeAttributes typeAttributes) =>
            (typeAttributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public;

        private static void ProcessEmbeddedDescriptors(EcmaModule module, NodeFactory factory, DependencyAnalyzerBase<NodeFactory> analyzer)
        {
            MetadataReader reader = module.MetadataReader;
            foreach (ManifestResourceHandle resourceHandle in reader.ManifestResources)
            {
                ManifestResource resource = reader.GetManifestResource(resourceHandle);
                if (!resource.Implementation.IsNil)
                    continue;

                if (reader.GetString(resource.Name) != "ILLink.Descriptors.xml")
                    continue;

                PEMemoryBlock resourceDirectory = module.PEReader.GetSectionData(
                    module.PEReader.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress);
                BlobReader blobReader = resourceDirectory.GetReader(
                    (int)resource.Offset,
                    resourceDirectory.Length - (int)resource.Offset);
                int length = (int)blobReader.ReadUInt32();

                unsafe
                {
                    using var stream = new UnmanagedMemoryStream(blobReader.CurrentPointer, length);
                    var dependencies = ReferenceAssemblyDescriptorReader.GetDependencies(
                        module.Context, stream, module, factory.Settings.FeatureSwitches, factory);
                    if (dependencies != null)
                    {
                        foreach (var dep in dependencies)
                            analyzer.AddRoot(dep.Node, dep.Reason);
                    }
                }

                break;
            }
        }

        /// <summary>
        /// Reads ILLink.Descriptors.xml from non-trimmed reference assemblies and creates
        /// VirtualMethodUse roots for virtual methods. Unlike the trimmed-assembly descriptor
        /// analyzer, this does not check IsModuleTrimmed since the assembly is a reference.
        /// </summary>
        private class ReferenceAssemblyDescriptorReader : ProcessLinkerXmlBase
        {
            private readonly NodeFactory _factory;
            private DependencyList _dependencies = new DependencyList();

            public static DependencyList GetDependencies(TypeSystemContext context, Stream content, EcmaModule owningModule,
                IReadOnlyDictionary<string, bool> featureSwitchValues, NodeFactory factory)
            {
                var rdr = new ReferenceAssemblyDescriptorReader(context, content, owningModule, featureSwitchValues, factory);
                rdr.ProcessXml(false);
                return rdr._dependencies;
            }

            private ReferenceAssemblyDescriptorReader(TypeSystemContext context, Stream content, EcmaModule owningModule,
                IReadOnlyDictionary<string, bool> featureSwitchValues, NodeFactory factory)
                : base(factory.Logger, context, content, default(ManifestResource), owningModule, "descriptor", featureSwitchValues)
            {
                _factory = factory;
            }

            protected override void ProcessAssembly(ModuleDesc assembly, System.Xml.XPath.XPathNavigator nav, bool warnOnUnresolvedTypes)
            {
                ProcessTypes(assembly, nav, warnOnUnresolvedTypes);
            }

            protected override void ProcessType(TypeDesc type, System.Xml.XPath.XPathNavigator nav)
            {
                ProcessTypeChildren(type, nav);
            }

            protected override void ProcessField(TypeDesc type, FieldDesc field, System.Xml.XPath.XPathNavigator nav)
            {
            }

            protected override void ProcessMethod(TypeDesc type, MethodDesc method, System.Xml.XPath.XPathNavigator nav, object customData)
            {
                if (method is EcmaMethod ecmaMethod && ecmaMethod.IsVirtual)
                {
                    MethodDesc slotMethod = MetadataVirtualMethodAlgorithm.FindSlotDefiningMethodForVirtualMethod(ecmaMethod);
                    _dependencies.Add(_factory.VirtualMethodUse((EcmaMethod)slotMethod),
                        "Virtual method use from reference assembly descriptor");
                }
            }

            protected override MethodDesc? GetMethod(TypeDesc type, string signature)
            {
                foreach (MethodDesc meth in type.GetAllMethods())
                {
                    if (signature == GetMethodSignature(meth, false))
                        return meth;
                }
                return null;
            }
        }
    }
}
