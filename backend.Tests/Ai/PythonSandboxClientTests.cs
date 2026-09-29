using Delegat.Services.Ai.Agent;

namespace Delegat.Tests.Ai;

[CollectionDefinition("sandbox-env", DisableParallelization = true)]
public sealed class SandboxEnvCollection;

[Collection("sandbox-env")]
public class PythonSandboxClientTests
{
    [Fact]
    public void CandidateUrls_EmptyUrlWithoutStack_IsOff()
    {
        lock (typeof(PythonSandboxClient))
        {
            var prevUrl = Environment.GetEnvironmentVariable("SANDBOX_URL");
            var prevStack = Environment.GetEnvironmentVariable("STACK_ID");
            var prevInstance = Environment.GetEnvironmentVariable("INSTANCE_ID");
            var prevAi = Environment.GetEnvironmentVariable("AI_ENABLED");
            var prevMysql = Environment.GetEnvironmentVariable("MYSQL_SERVER");
            var prevMinio = Environment.GetEnvironmentVariable("MINIO_ENDPOINT");
            Environment.SetEnvironmentVariable("SANDBOX_URL", null);
            Environment.SetEnvironmentVariable("STACK_ID", null);
            Environment.SetEnvironmentVariable("INSTANCE_ID", null);
            Environment.SetEnvironmentVariable("MYSQL_SERVER", null);
            Environment.SetEnvironmentVariable("MINIO_ENDPOINT", null);
            Environment.SetEnvironmentVariable("AI_ENABLED", "true");
            try
            {
                Assert.False(PythonSandboxClient.IsConfigured());
                Assert.Empty(PythonSandboxClient.CandidateUrls());
            }
            finally
            {
                Environment.SetEnvironmentVariable("SANDBOX_URL", prevUrl);
                Environment.SetEnvironmentVariable("STACK_ID", prevStack);
                Environment.SetEnvironmentVariable("INSTANCE_ID", prevInstance);
                Environment.SetEnvironmentVariable("MYSQL_SERVER", prevMysql);
                Environment.SetEnvironmentVariable("MINIO_ENDPOINT", prevMinio);
                Environment.SetEnvironmentVariable("AI_ENABLED", prevAi);
            }
        }
    }

    [Fact]
    public void CandidateUrls_EmptyUrlWithStackAndAi_UsesContainerName()
    {
        lock (typeof(PythonSandboxClient))
        {
            var prevUrl = Environment.GetEnvironmentVariable("SANDBOX_URL");
            var prevStack = Environment.GetEnvironmentVariable("STACK_ID");
            var prevInstance = Environment.GetEnvironmentVariable("INSTANCE_ID");
            var prevAi = Environment.GetEnvironmentVariable("AI_ENABLED");
            Environment.SetEnvironmentVariable("SANDBOX_URL", "");
            Environment.SetEnvironmentVariable("STACK_ID", "abcdef012345");
            Environment.SetEnvironmentVariable("INSTANCE_ID", null);
            Environment.SetEnvironmentVariable("AI_ENABLED", "true");
            try
            {
                Assert.True(PythonSandboxClient.IsConfigured());
                var urls = PythonSandboxClient.CandidateUrls();
                Assert.Equal(["http://d-abcdef012345-sandbox:8080"], urls);
            }
            finally
            {
                Environment.SetEnvironmentVariable("SANDBOX_URL", prevUrl);
                Environment.SetEnvironmentVariable("STACK_ID", prevStack);
                Environment.SetEnvironmentVariable("INSTANCE_ID", prevInstance);
                Environment.SetEnvironmentVariable("AI_ENABLED", prevAi);
            }
        }
    }

    [Fact]
    public void CandidateUrls_AiOff_IgnoresExplicitUrl()
    {
        lock (typeof(PythonSandboxClient))
        {
            var prevUrl = Environment.GetEnvironmentVariable("SANDBOX_URL");
            var prevStack = Environment.GetEnvironmentVariable("STACK_ID");
            var prevAi = Environment.GetEnvironmentVariable("AI_ENABLED");
            Environment.SetEnvironmentVariable("SANDBOX_URL", "http://d-abcdef012345-sandbox:8080");
            Environment.SetEnvironmentVariable("STACK_ID", "abcdef012345");
            Environment.SetEnvironmentVariable("AI_ENABLED", "false");
            try
            {
                Assert.False(PythonSandboxClient.IsConfigured());
                Assert.Empty(PythonSandboxClient.CandidateUrls());
            }
            finally
            {
                Environment.SetEnvironmentVariable("SANDBOX_URL", prevUrl);
                Environment.SetEnvironmentVariable("STACK_ID", prevStack);
                Environment.SetEnvironmentVariable("AI_ENABLED", prevAi);
            }
        }
    }

    [Fact]
    public void CandidateUrls_LitePlan_DoesNotInventSidecar()
    {
        lock (typeof(PythonSandboxClient))
        {
            var prevUrl = Environment.GetEnvironmentVariable("SANDBOX_URL");
            var prevStack = Environment.GetEnvironmentVariable("STACK_ID");
            var prevAi = Environment.GetEnvironmentVariable("AI_ENABLED");
            Environment.SetEnvironmentVariable("SANDBOX_URL", null);
            Environment.SetEnvironmentVariable("STACK_ID", "abcdef012345");
            Environment.SetEnvironmentVariable("AI_ENABLED", "false");
            try
            {
                Assert.False(PythonSandboxClient.IsConfigured());
            }
            finally
            {
                Environment.SetEnvironmentVariable("SANDBOX_URL", prevUrl);
                Environment.SetEnvironmentVariable("STACK_ID", prevStack);
                Environment.SetEnvironmentVariable("AI_ENABLED", prevAi);
            }
        }
    }

    [Fact]
    public void ResolveStackId_FromInstanceGuid()
    {
        lock (typeof(PythonSandboxClient))
        {
            var prevStack = Environment.GetEnvironmentVariable("STACK_ID");
            var prevInstance = Environment.GetEnvironmentVariable("INSTANCE_ID");
            Environment.SetEnvironmentVariable("STACK_ID", null);
            Environment.SetEnvironmentVariable("INSTANCE_ID", "abcdef01-2345-6789-abcd-ef0123456789");
            try
            {
                Assert.Equal("abcdef012345", PythonSandboxClient.ResolveStackId());
            }
            finally
            {
                Environment.SetEnvironmentVariable("STACK_ID", prevStack);
                Environment.SetEnvironmentVariable("INSTANCE_ID", prevInstance);
            }
        }
    }

    [Fact]
    public void ResolveStackId_FromControlPlaneInstanceId_KeepsFullStack()
    {
        lock (typeof(PythonSandboxClient))
        {
            var prevStack = Environment.GetEnvironmentVariable("STACK_ID");
            var prevInstance = Environment.GetEnvironmentVariable("INSTANCE_ID");
            var prevMysql = Environment.GetEnvironmentVariable("MYSQL_SERVER");
            Environment.SetEnvironmentVariable("STACK_ID", null);
            Environment.SetEnvironmentVariable("INSTANCE_ID", "inst_bff0ba4989f9464888e1a67895443adc");
            Environment.SetEnvironmentVariable("MYSQL_SERVER", "mysql");
            try
            {
                Assert.Equal("bff0ba4989f9464888e1a67895443adc", PythonSandboxClient.ResolveStackId());
            }
            finally
            {
                Environment.SetEnvironmentVariable("STACK_ID", prevStack);
                Environment.SetEnvironmentVariable("INSTANCE_ID", prevInstance);
                Environment.SetEnvironmentVariable("MYSQL_SERVER", prevMysql);
            }
        }
    }

    [Fact]
    public void CandidateUrls_ControlPlaneInstance_UsesContainerNameOnly()
    {
        lock (typeof(PythonSandboxClient))
        {
            var prevUrl = Environment.GetEnvironmentVariable("SANDBOX_URL");
            var prevStack = Environment.GetEnvironmentVariable("STACK_ID");
            var prevInstance = Environment.GetEnvironmentVariable("INSTANCE_ID");
            var prevAi = Environment.GetEnvironmentVariable("AI_ENABLED");
            Environment.SetEnvironmentVariable("SANDBOX_URL", "");
            Environment.SetEnvironmentVariable("STACK_ID", null);
            Environment.SetEnvironmentVariable("INSTANCE_ID", "inst_bff0ba4989f9464888e1a67895443adc");
            Environment.SetEnvironmentVariable("AI_ENABLED", "true");
            try
            {
                var urls = PythonSandboxClient.CandidateUrls();
                Assert.Equal(
                    [
                        "http://d-bff0ba4989f9464888e1a67895443adc-sandbox:8080",
                        "http://d-bff0ba4989f9-sandbox:8080",
                    ],
                    urls);
            }
            finally
            {
                Environment.SetEnvironmentVariable("SANDBOX_URL", prevUrl);
                Environment.SetEnvironmentVariable("STACK_ID", prevStack);
                Environment.SetEnvironmentVariable("INSTANCE_ID", prevInstance);
                Environment.SetEnvironmentVariable("AI_ENABLED", prevAi);
            }
        }
    }

    [Fact]
    public void ResolveStackId_FromMysqlServerHost()
    {
        lock (typeof(PythonSandboxClient))
        {
            var prevStack = Environment.GetEnvironmentVariable("STACK_ID");
            var prevInstance = Environment.GetEnvironmentVariable("INSTANCE_ID");
            var prevMysql = Environment.GetEnvironmentVariable("MYSQL_SERVER");
            var prevMinio = Environment.GetEnvironmentVariable("MINIO_ENDPOINT");
            Environment.SetEnvironmentVariable("STACK_ID", null);
            Environment.SetEnvironmentVariable("INSTANCE_ID", null);
            Environment.SetEnvironmentVariable("MYSQL_SERVER", "mysql-bff0ba4989f9464888e1a67895443adc");
            Environment.SetEnvironmentVariable("MINIO_ENDPOINT", null);
            try
            {
                Assert.Equal("bff0ba4989f9464888e1a67895443adc", PythonSandboxClient.ResolveStackId());
                Assert.Null(PythonSandboxClient.StackIdFromServiceHost("mysql"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("STACK_ID", prevStack);
                Environment.SetEnvironmentVariable("INSTANCE_ID", prevInstance);
                Environment.SetEnvironmentVariable("MYSQL_SERVER", prevMysql);
                Environment.SetEnvironmentVariable("MINIO_ENDPOINT", prevMinio);
            }
        }
    }

    [Fact]
    public void ResolveAgentBaseUrl_RewritesCpAliasToContainerName()
    {
        lock (typeof(PythonSandboxClient))
        {
            var prevAgent = Environment.GetEnvironmentVariable("SANDBOX_AGENT_BASE_URL");
            var prevStack = Environment.GetEnvironmentVariable("STACK_ID");
            Environment.SetEnvironmentVariable("SANDBOX_AGENT_BASE_URL", "http://api-abcdef012345:5000");
            Environment.SetEnvironmentVariable("STACK_ID", "abcdef012345");
            try
            {
                Assert.Equal("http://d-abcdef012345-api:5000", PythonSandboxClient.ResolveAgentBaseUrl());
            }
            finally
            {
                Environment.SetEnvironmentVariable("SANDBOX_AGENT_BASE_URL", prevAgent);
                Environment.SetEnvironmentVariable("STACK_ID", prevStack);
            }
        }
    }

    [Fact]
    public void ResolveAgentBaseUrl_UsesApiContainerName()
    {
        lock (typeof(PythonSandboxClient))
        {
            var prevAgent = Environment.GetEnvironmentVariable("SANDBOX_AGENT_BASE_URL");
            var prevPublic = Environment.GetEnvironmentVariable("PUBLIC_BASE_URL");
            var prevStack = Environment.GetEnvironmentVariable("STACK_ID");
            Environment.SetEnvironmentVariable("SANDBOX_AGENT_BASE_URL", null);
            Environment.SetEnvironmentVariable("PUBLIC_BASE_URL", null);
            Environment.SetEnvironmentVariable("STACK_ID", "abcdef012345");
            try
            {
                Assert.Equal("http://d-abcdef012345-api:5000", PythonSandboxClient.ResolveAgentBaseUrl());
            }
            finally
            {
                Environment.SetEnvironmentVariable("SANDBOX_AGENT_BASE_URL", prevAgent);
                Environment.SetEnvironmentVariable("PUBLIC_BASE_URL", prevPublic);
                Environment.SetEnvironmentVariable("STACK_ID", prevStack);
            }
        }
    }
}
