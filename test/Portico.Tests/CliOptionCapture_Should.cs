using System;
using Xunit;

namespace Portico;

// POR-66. The capture-interface decision: ICliOptionCapture is the root of the family and EVERY
// concrete capture implements it (via the base record CliOptionCapture). This pins that so the
// "shared interface for every kind of parsed option" doc stays honest — the survey found it was true
// for only 4 of the 6 shapes.
// ReSharper disable once InconsistentNaming
public sealed class CliOptionCapture_Should
{
    [Theory]
    [InlineData(typeof(CliFlagOptionCapture))]
    [InlineData(typeof(CliScalarOptionCapture))]
    [InlineData(typeof(CliCollectionOptionCapture))]
    [InlineData(typeof(CliKeyValueOptionCapture))]
    [InlineData(typeof(CliKeyFlagOptionCapture))]
    [InlineData(typeof(CliKeyCollectionOptionCapture))]
    public void Implement_ICliOptionCapture(Type captureType)
    {
        Assert.True(
            typeof(ICliOptionCapture).IsAssignableFrom(captureType),
            $"{captureType.Name} must implement ICliOptionCapture (the shared root of the capture family).");
    }
}
