using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ClientLoadCheckTests
{
    [Fact]
    public void ATypeWithItsAssembly_SplitsAtTheFirstComma() =>
        Assert.Equal(("EFT.Player", "Assembly-CSharp"),
            ClientLoadCheck.SplitTypeName("EFT.Player, Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null"));

    [Fact]
    public void ANestedType_KeepsThePlus() =>
        Assert.Equal(("EFT.Player+FirearmController", "Assembly-CSharp"),
            ClientLoadCheck.SplitTypeName("EFT.Player+FirearmController, Assembly-CSharp"));

    [Fact]
    public void ATypeWithNoAssembly_HasNone() =>
        Assert.Equal(("EFT.Player", null), ClientLoadCheck.SplitTypeName("EFT.Player"));

    [Fact]
    public void AGenericInstance_ComesBackAsItsGenericType_WithTheOuterAssembly() =>
        Assert.Equal(("System.Collections.Generic.List`1", "mscorlib"),
            ClientLoadCheck.SplitTypeName("System.Collections.Generic.List`1[[EFT.Player, Assembly-CSharp, Version=0.0.0.0]], mscorlib, Version=4.0.0.0"));

    [Fact]
    public void AGenericInstanceWithTwoArguments_SplitsOutsideTheBrackets() =>
        Assert.Equal(("System.Collections.Generic.Dictionary`2", "mscorlib"),
            ClientLoadCheck.SplitTypeName("System.Collections.Generic.Dictionary`2[[System.String, mscorlib],[EFT.Player, Assembly-CSharp]], mscorlib"));

    [Fact]
    public void SpacesAroundTheParts_AreTrimmed() =>
        Assert.Equal(("EFT.Player", "Assembly-CSharp"), ClientLoadCheck.SplitTypeName("  EFT.Player ,  Assembly-CSharp "));
}
