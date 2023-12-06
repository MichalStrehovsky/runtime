// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Internal.TypeSystem;

namespace ILCompiler
{
    public class ReadOnlyFieldPolicy
    {
        public virtual bool IsReadOnly(FieldDesc field) => field.IsInitOnly;

        /// <summary>
        /// This might look the same as IsReadOnly, but this will also return false for readonly fields
        /// that are set in a lazy static constructor. This method will only return true if the value cannot possibly
        /// change at runtime. This will return true for fields that either have their value set in the preinit data blob
        /// or are default-initialized, and there's no code that could change them at runtime (not even a lazy cctor).
        /// </summary>
        public virtual bool IsNeverWrittenAtRuntime(FieldDesc field) => false;
    }

    public sealed class StaticReadOnlyFieldPolicy : ReadOnlyFieldPolicy
    {
        public override bool IsReadOnly(FieldDesc field) => field.IsStatic && field.IsInitOnly;
    }
}
