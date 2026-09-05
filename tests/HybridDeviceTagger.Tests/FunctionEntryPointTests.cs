using System.Reflection;

namespace HybridDeviceTagger.Tests;

public sealed class FunctionEntryPointTests
{
    [Theory]
    [InlineData(typeof(DeviceTaggerFunction))]
    [InlineData(typeof(PasswordRotationFunction))]
    public void TimerFunction_ExposesOnePublicRunAsyncMethod(Type functionType)
    {
        var runMethods = functionType.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == "RunAsync");

        Assert.Single(runMethods);
    }
}