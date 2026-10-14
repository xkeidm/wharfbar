using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Owns <see cref="System.Windows.Application.Current"/>. Must not overlap other STA tests:
/// a control created on a second thread throws once that application exists.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfApplicationCollection
{
    internal const string Name = "WpfApplication";
}
