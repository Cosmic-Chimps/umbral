using System;
using System.Linq;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Xunit;

namespace CosmicChimps.Aspire.Hosting.Umbral.Tests;

// Umbral's data is one folder, /data. These pin how the two ways of keeping it combine. Aspire
// refuses a second, different bind mount on a target itself, but lets a volume sit beside anything
// at the same target, so the package refuses every conflict itself, naming /data and the calls.
public sealed class UmbralDataMountTests
{
    private static IResourceBuilder<UmbralResource> NewUmbral() =>
        DistributedApplication.CreateBuilder([]).AddUmbral("umbral");

    private static ContainerMountAnnotation[] DataMounts(IResourceBuilder<UmbralResource> umbral) =>
        umbral.Resource.Annotations.OfType<ContainerMountAnnotation>().Where(m => m.Target == "/data").ToArray();

    [Fact]
    public void One_data_bind_mount_mounts_the_folder_at_data()
    {
        var umbral = NewUmbral().WithDataBindMount("/srv/umbral");

        var mount = Assert.Single(DataMounts(umbral));
        Assert.Equal(ContainerMountType.BindMount, mount.Type);
        Assert.Equal("/srv/umbral", mount.Source);
    }

    [Fact]
    public void One_data_volume_mounts_the_volume_at_data()
    {
        var umbral = NewUmbral().WithDataVolume("umbral-data");

        var mount = Assert.Single(DataMounts(umbral));
        Assert.Equal(ContainerMountType.Volume, mount.Type);
        Assert.Equal("umbral-data", mount.Source);
    }

    [Fact]
    public void The_same_bind_mount_twice_is_fine_and_mounts_once()
    {
        var umbral = NewUmbral().WithDataBindMount("/srv/umbral").WithDataBindMount("/srv/umbral");

        Assert.Single(DataMounts(umbral));
    }

    [Fact]
    public void The_same_relative_bind_mount_twice_is_fine_and_mounts_once()
    {
        var umbral = NewUmbral().WithDataBindMount("data").WithDataBindMount("data");

        Assert.Single(DataMounts(umbral));
    }

    [Fact]
    public void The_same_volume_twice_is_fine_and_mounts_once()
    {
        var umbral = NewUmbral().WithDataVolume("umbral-data").WithDataVolume("umbral-data");

        Assert.Single(DataMounts(umbral));
    }

    [Fact]
    public void A_generated_volume_name_twice_is_fine_and_mounts_once()
    {
        var umbral = NewUmbral().WithDataVolume().WithDataVolume();

        Assert.Single(DataMounts(umbral));
    }

    [Fact]
    public void Two_different_bind_mounts_fail_naming_data_and_the_call()
    {
        var umbral = NewUmbral().WithDataBindMount("/srv/a");

        var ex = Assert.Throws<InvalidOperationException>(() => umbral.WithDataBindMount("/srv/b"));

        Assert.Contains("/data", ex.Message, StringComparison.Ordinal);
        Assert.Contains("WithDataBindMount", ex.Message, StringComparison.Ordinal);
        Assert.Single(DataMounts(umbral));
    }

    [Fact]
    public void Two_different_volumes_fail_naming_data_and_the_call()
    {
        var umbral = NewUmbral().WithDataVolume("one");

        var ex = Assert.Throws<InvalidOperationException>(() => umbral.WithDataVolume("two"));

        Assert.Contains("/data", ex.Message, StringComparison.Ordinal);
        Assert.Contains("WithDataVolume", ex.Message, StringComparison.Ordinal);
        Assert.Single(DataMounts(umbral));
    }

    [Fact]
    public void A_volume_after_a_bind_mount_fails_naming_both_calls()
    {
        var umbral = NewUmbral().WithDataBindMount("/srv/umbral");

        var ex = Assert.Throws<InvalidOperationException>(() => umbral.WithDataVolume("umbral-data"));

        Assert.Contains("/data", ex.Message, StringComparison.Ordinal);
        Assert.Contains("WithDataVolume", ex.Message, StringComparison.Ordinal);
        Assert.Contains("WithDataBindMount", ex.Message, StringComparison.Ordinal);
        Assert.Single(DataMounts(umbral));
    }

    [Fact]
    public void A_bind_mount_after_a_volume_fails_naming_both_calls()
    {
        var umbral = NewUmbral().WithDataVolume("umbral-data");

        var ex = Assert.Throws<InvalidOperationException>(() => umbral.WithDataBindMount("/srv/umbral"));

        Assert.Contains("/data", ex.Message, StringComparison.Ordinal);
        Assert.Contains("WithDataVolume", ex.Message, StringComparison.Ordinal);
        Assert.Contains("WithDataBindMount", ex.Message, StringComparison.Ordinal);
        Assert.Single(DataMounts(umbral));
    }
}
