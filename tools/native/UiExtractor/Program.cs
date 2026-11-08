// UiExtractor: moves engine (Chummer/Backend) methods whose signatures involve UI types (WinForms/WPF types, or
// types defined under Chummer/Forms or Chummer/Controls) into UI-side static classes under
// Chummer/Forms/UiExtensions, as extension methods. Call sites keep compiling unchanged:
//   - instance methods become `public static R M(this C objThis, ...)`; implicit member access in their bodies is
//     rewritten to `objThis.X`, and remaining unqualified calls inside C become `this.M(...)`;
//   - static methods move as static methods; qualified call sites `C.M` are re-pointed at the new class;
//   - private/protected members the moved bodies touch are widened to internal (the engine assembly will later
//     grant InternalsVisibleTo to the legacy WinForms UI).
// Methods that cannot be moved mechanically (overrides, interface implementations, generic containing types,
// `base.` access, name collisions with members that stay) are reported and left alone.
//
// Usage: UiExtractor <repo root> [--dry-run]

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Build.Locator;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        MSBuildLocator.RegisterDefaults();
        return await Extractor.Run(args).ConfigureAwait(false);
    }
}
