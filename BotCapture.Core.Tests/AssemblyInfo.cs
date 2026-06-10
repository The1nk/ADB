using Xunit;

// These tests exercise System.Drawing / GDI+ (Bitmap allocation, Crop.Save to disk via PreviewConfirmViewModel,
// FrameCapturer) which use process-global, non-thread-safe native state. Running them concurrently — especially
// under the system-wide load of a full-solution `dotnet test` — intermittently throws GDI+ "generic error" from a
// save/allocate, which surfaces as a spurious test failure. The production code (PreviewConfirmViewModel) is only
// ever driven single-threaded on the WPF UI thread, so serializing this assembly's tests models real usage and
// removes the concurrency artifact without masking any production defect.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
