// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Internal.TypeSystem
{
    // Implements method canonicalization
    public partial class MethodDesc
    {
        /// <summary>
        /// Gets a value indicating whether this is a shared method body.
        /// </summary>
        public bool IsSharedByGenericInstantiations
        {
            get
            {
                return IsCanonicalMethod(CanonicalFormKind.Any);
            }
        }

        /// <summary>
        /// Gets a value indicating whether the method requires a hidden instantiation argument in addition
        /// to the formal arguments defined in the method signature.
        /// </summary>
        public bool RequiresInstArg()
        {
            return Context.RequiresInstArg(this);
        }

        /// <summary>
        /// Gets a value indicating whether the method acquires the generic context from a hidden
        /// instantiation argument that points to the method's generic dictionary.
        /// </summary>
        public bool RequiresInstMethodDescArg()
        {
            return HasInstantiation && RequiresInstArg();
        }

        /// <summary>
        /// Gets a value indicating whether the method acquires the generic context from a hidden
        /// instantiation argument that points to the generic dictionary of the method's owning type.
        /// </summary>
        public bool RequiresInstMethodTableArg()
        {
            return !HasInstantiation && RequiresInstArg();
        }

        /// <summary>
        /// Gets a value indicating whether the method acquires the generic context from the this pointer.
        /// </summary>
        public bool AcquiresInstMethodTableFromThis()
        {
            return IsSharedByGenericInstantiations &&
                !HasInstantiation &&
                !Signature.IsStatic &&
                !OwningType.IsValueType &&
                !(OwningType.IsInterface && !IsAbstract);
        }

        public virtual bool IsCanonicalMethod(CanonicalFormKind policy)
        {
            return false;
        }

        /// <summary>
        /// Returns the result of canonicalizing this method over the given kind of Canon
        /// </summary>
        public virtual MethodDesc GetCanonMethodTarget(CanonicalFormKind kind)
        {
            return this;
        }
    }
}
