using Xunit;

// Most of this assembly's tests construct, save, crop, and encode System.Drawing.Bitmap images
// (PreviewConfirmViewModel, RegionSelectionViewModel, ThumbnailEncoder, CaptureSaver). GDI+ is a
// process-global, non-thread-safe resource: when xUnit runs these test classes as parallel
// collections, a transient GDI+ failure in one can surface in another (e.g. TestMatch's Crop.Save
// throwing, caught and degraded to a null-Score outcome — a flake seen only under full-suite
// parallelism, never in isolation). The production code paths are single-threaded (WPF UI thread),
// so the concurrency exists only in the test runner. Serialize this assembly's collections to remove
// it. Other test assemblies still parallelize; this one is small and fast, so the cost is negligible.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
