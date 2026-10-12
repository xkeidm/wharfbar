using System.IO;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class PackageLogoSelectionTests
{
    [Fact]
    public void TrySplitAumid_AcceptsAPackageIdAndRejectsAPath()
    {
        Assert.True(PackageLogoSelection.TrySplitAumid(
            "Contoso.App_8wekyb3d8bbwe!App",
            out string family,
            out string app));
        Assert.Equal("Contoso.App_8wekyb3d8bbwe", family);
        Assert.Equal("App", app);

        Assert.False(PackageLogoSelection.TrySplitAumid(@"C:\Windows\notepad.exe", out _, out _));
        Assert.False(PackageLogoSelection.TrySplitAumid("no-bang", out _, out _));
        Assert.False(PackageLogoSelection.TrySplitAumid("  ", out _, out _));
    }

    [Fact]
    public void CandidateFiles_PrefersScaleVariantsThenThePlainFile()
    {
        IReadOnlyList<string> files = PackageLogoSelection.CandidateFiles(
            @"C:\Pkg",
            "ms-appx:///Assets/StoreLogo.png");

        Assert.Equal(
            Path.Combine(@"C:\Pkg", "Assets", "StoreLogo.scale-200.png"),
            files[0]);
        Assert.Equal(
            Path.Combine(@"C:\Pkg", "Assets", "StoreLogo.png"),
            files[^1]);
    }

    [Fact]
    public void FirstExisting_SkipsAMissingScaleAndAThrowingProbe()
    {
        IReadOnlyList<string> files = PackageLogoSelection.CandidateFiles(
            @"C:\Pkg",
            "ms-appx:///Assets/StoreLogo.png");
        string scale100 = Path.Combine(@"C:\Pkg", "Assets", "StoreLogo.scale-100.png");

        Assert.Equal(scale100, PackageLogoSelection.FirstExisting(
            files,
            path => path.EndsWith("StoreLogo.scale-100.png", StringComparison.Ordinal)));

        Assert.Equal(files[1], PackageLogoSelection.FirstExisting(files, path =>
        {
            if (path == files[0])
            {
                throw new IOException("denied");
            }

            return path == files[1];
        }));

        Assert.Null(PackageLogoSelection.FirstExisting(files, _ => false));
    }

    [Fact]
    public void IndirectSource_WrapsMsResourceAndLeavesFilesAlone()
    {
        Assert.Equal(
            "@{PkgFull?ms-resource://App/Files/Logo.png}",
            PackageLogoSelection.IndirectSource("PkgFull", "ms-resource://App/Files/Logo.png"));
        Assert.Equal("@{already}", PackageLogoSelection.IndirectSource("PkgFull", "@{already}"));
        Assert.Null(PackageLogoSelection.IndirectSource("PkgFull", "ms-appx:///Assets/Logo.png"));
        Assert.Empty(PackageLogoSelection.CandidateFiles(@"C:\Pkg", "ms-resource://App/Files/Logo.png"));
        Assert.Empty(PackageLogoSelection.CandidateFiles(null, "ms-appx:///Assets/Logo.png"));
    }

    [Fact]
    public void MalformedLogo_DoesNotThrow()
    {
        Exception? error = Record.Exception(() =>
        {
            _ = PackageLogoSelection.CandidateFiles(@"C:\Pkg", "@@@");
            _ = PackageLogoSelection.CandidateFiles(@"C:\Pkg", "file:not a uri");
            _ = PackageLogoSelection.IndirectSource(null, "ms-resource://App/Logo");
            _ = PackageLogoSelection.TrySplitAumid("A!B!C", out _, out _);
        });
        Assert.Null(error);
        Assert.Empty(PackageLogoSelection.CandidateFiles(@"C:\Pkg", "@@@"));
    }
}
