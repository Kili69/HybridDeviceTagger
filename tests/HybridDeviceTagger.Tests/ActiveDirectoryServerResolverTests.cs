using HybridDeviceTagger.Services;

namespace HybridDeviceTagger.Tests;

public sealed class ActiveDirectoryServerResolverTests
{
    [Fact]
    public void OrderByPriorityAndWeight_PutsLowerPriorityValuesFirst()
    {
        var lowerPriority = CreateRecord("dc01.example.com", priority: 0, weight: 1);
        var higherPriority = CreateRecord("dc02.example.com", priority: 10, weight: 100);

        var result = ActiveDirectoryServerResolver.OrderByPriorityAndWeight(
            [higherPriority, lowerPriority]);

        Assert.Equal([lowerPriority, higherPriority], result);
    }

    [Fact]
    public void OrderByPriorityAndWeight_PreservesEveryServer()
    {
        var records = new[]
        {
            CreateRecord("dc01.example.com", priority: 0, weight: 0),
            CreateRecord("dc02.example.com", priority: 0, weight: 10),
            CreateRecord("dc03.example.com", priority: 0, weight: 20)
        };

        var result = ActiveDirectoryServerResolver.OrderByPriorityAndWeight(records);

        Assert.Equal(records.Length, result.Count);
        Assert.Empty(records.Except(result));
    }

    private static ActiveDirectorySrvTarget CreateRecord(
        string target,
        ushort priority,
        ushort weight) =>
        new(target, priority, weight);
}
