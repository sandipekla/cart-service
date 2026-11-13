using System.Reflection;
using System.Runtime.InteropServices;
using Carts.Domain;

namespace Carts.UnitTests;

public class DomainDependencyTests
{
    [Fact]
    public void The_domain_uses_nothing_but_the_dotnet_runtime()
    {
        Assert.Empty(ReferencesOutsideTheRuntime(typeof(Cart).Assembly));
    }

    [Fact]
    public void The_check_itself_notices_an_assembly_that_is_not_part_of_the_runtime()
    {
        Assert.Contains("Carts.Domain", ReferencesOutsideTheRuntime(typeof(DomainDependencyTests).Assembly));
    }

    // An assembly belongs to the runtime when its file sits in the runtime's own directory.
    private static string[] ReferencesOutsideTheRuntime(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !File.Exists(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), name + ".dll")))];
}
