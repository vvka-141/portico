using System.ComponentModel;
using System.Linq;
using Xunit;

namespace Portico.Reflection;

// ReSharper disable once InconsistentNaming
public sealed class CliMethodInfo_Get_Should
{
    [Fact]
    public void HandlePgUpCase()
    {
        IPgUpTestCase.Test();
    }

    public interface IPgUpTestCase
    {
        [CliRoute("init {projectDir}")]
        [CliCommandExample("init .")]
        [CliCommandExample("init /src/database --template basic")]
        public void Initialize(
            [CliArgument("Project directory path", Name = "ProjectDir")] string projectDir,
            [CliOption("--verbose")] CliFlag? verbose);

        internal static void Test()
        {
            var methods = CliMethodInfo.Get(typeof(IPgUpTestCase), CliContext.Empty);
            Assert.Single(methods);
            TestInitializeMethod(methods.Single(m => m.Name.Equals(nameof(Initialize))));
        }

        private static void TestInitializeMethod(CliMethodInfo method)
        {
            Assert.False(method.IsStatic);
            Assert.Equal(2, method.Examples.Length);

            var arguments = method.GetParameters().OfType<CliArgumentParameterInfo>().ToList();
            var options = method.GetParameters().OfType<CliOptionParameterInfo>().ToList();
            var optionBundles = method.GetParameters().OfType<CliOptionsParameterInfo>().ToList();

            Assert.Single(arguments);
            var filePathArgument = arguments.Single();
            Assert.Equal("projectDir", filePathArgument.Name);
            Assert.Equal("ProjectDir", filePathArgument.CliArgumentName);
            Assert.Equal("Project directory path", filePathArgument.Description);
            Assert.Equal(1, filePathArgument.CliRoutePosition);
            Assert.True(filePathArgument.TypeConverter is StringConverter);
        }
    }
}