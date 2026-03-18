using System.Text.Json;
using System.Text.Json.Nodes;
using Clawsharp.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawsharp.Tests.Unit.Config;

/// <summary>
/// Tests for <see cref="ConfigMigrator"/> which renames legacy config property names
/// to their current equivalents before deserialization.
/// </summary>
[TestFixture]
public sealed class ConfigMigratorTests
{
    private static readonly ILogger Logger = NullLogger.Instance;

    // -- rateLimitWindowSeconds → rateLimitWindow --

    [Test]
    public void MigrateLegacyKeys_RateLimitWindowSeconds_RenamedToRateLimitWindow()
    {
        var json = """
        {
            "agents": {
                "defaults": {
                    "rateLimitWindowSeconds": 60
                }
            }
        }
        """;

        var result = ConfigMigrator.MigrateLegacyKeys(json, Logger);
        var root = JsonNode.Parse(result)!;

        root["agents"]!["defaults"]!["rateLimitWindow"].ShouldNotBeNull();
        // Numeric seconds are converted to TimeSpan strings for IConfiguration.Bind() compatibility.
        root["agents"]!["defaults"]!["rateLimitWindow"]!.GetValue<string>().ShouldBe(TimeSpan.FromSeconds(60).ToString());
        root["agents"]!["defaults"]!["rateLimitWindowSeconds"].ShouldBeNull();
    }

    // -- healthCheck.intervalSeconds → interval --

    [Test]
    public void MigrateLegacyKeys_HealthCheckIntervalSeconds_RenamedToInterval()
    {
        var json = """
        {
            "agents": {
                "defaults": {
                    "healthCheck": {
                        "intervalSeconds": 300
                    }
                }
            }
        }
        """;

        var result = ConfigMigrator.MigrateLegacyKeys(json, Logger);
        var root = JsonNode.Parse(result)!;
        var hc = root["agents"]!["defaults"]!["healthCheck"]!;

        hc["interval"].ShouldNotBeNull();
        hc["interval"]!.GetValue<string>().ShouldBe(TimeSpan.FromSeconds(300).ToString());
        hc["intervalSeconds"].ShouldBeNull();
    }

    // -- resilience properties --

    [Test]
    public void MigrateLegacyKeys_ResilienceRequestTimeoutSeconds_Renamed()
    {
        var json = """
        {
            "agents": {
                "defaults": {
                    "resilience": {
                        "requestTimeoutSeconds": 120
                    }
                }
            }
        }
        """;

        var result = ConfigMigrator.MigrateLegacyKeys(json, Logger);
        var root = JsonNode.Parse(result)!;
        var res = root["agents"]!["defaults"]!["resilience"]!;

        res["requestTimeout"].ShouldNotBeNull();
        res["requestTimeout"]!.GetValue<string>().ShouldBe(TimeSpan.FromSeconds(120).ToString());
        res["requestTimeoutSeconds"].ShouldBeNull();
    }

    [Test]
    public void MigrateLegacyKeys_RetryDelaySeconds_Renamed()
    {
        var json = """
        {
            "agents": {
                "defaults": {
                    "resilience": {
                        "retry": {
                            "delaySeconds": 1.0,
                            "maxDelaySeconds": 30.0
                        }
                    }
                }
            }
        }
        """;

        var result = ConfigMigrator.MigrateLegacyKeys(json, Logger);
        var root = JsonNode.Parse(result)!;
        var retry = root["agents"]!["defaults"]!["resilience"]!["retry"]!;

        retry["delay"].ShouldNotBeNull();
        retry["delay"]!.GetValue<string>().ShouldBe(TimeSpan.FromSeconds(1.0).ToString());
        retry["delaySeconds"].ShouldBeNull();

        retry["maxDelay"].ShouldNotBeNull();
        retry["maxDelay"]!.GetValue<string>().ShouldBe(TimeSpan.FromSeconds(30.0).ToString());
        retry["maxDelaySeconds"].ShouldBeNull();
    }

    [Test]
    public void MigrateLegacyKeys_CircuitBreakerDurationSeconds_Renamed()
    {
        var json = """
        {
            "agents": {
                "defaults": {
                    "resilience": {
                        "circuitBreaker": {
                            "breakDurationSeconds": 30.0,
                            "samplingDurationSeconds": 60
                        }
                    }
                }
            }
        }
        """;

        var result = ConfigMigrator.MigrateLegacyKeys(json, Logger);
        var root = JsonNode.Parse(result)!;
        var cb = root["agents"]!["defaults"]!["resilience"]!["circuitBreaker"]!;

        cb["breakDuration"].ShouldNotBeNull();
        cb["breakDuration"]!.GetValue<string>().ShouldBe(TimeSpan.FromSeconds(30.0).ToString());
        cb["breakDurationSeconds"].ShouldBeNull();

        cb["samplingDuration"].ShouldNotBeNull();
        cb["samplingDuration"]!.GetValue<string>().ShouldBe(TimeSpan.FromSeconds(60).ToString());
        cb["samplingDurationSeconds"].ShouldBeNull();
    }

    // -- secrets.onePassword/bitwarden.timeoutSeconds → timeout --

    [Test]
    public void MigrateLegacyKeys_OnePasswordTimeoutSeconds_Renamed()
    {
        var json = """
        {
            "secrets": {
                "onePassword": {
                    "timeoutSeconds": 10
                }
            }
        }
        """;

        var result = ConfigMigrator.MigrateLegacyKeys(json, Logger);
        var root = JsonNode.Parse(result)!;
        var op = root["secrets"]!["onePassword"]!;

        op["timeout"].ShouldNotBeNull();
        op["timeout"]!.GetValue<string>().ShouldBe(TimeSpan.FromSeconds(10).ToString());
        op["timeoutSeconds"].ShouldBeNull();
    }

    [Test]
    public void MigrateLegacyKeys_BitwardenTimeoutSeconds_Renamed()
    {
        var json = """
        {
            "secrets": {
                "bitwarden": {
                    "timeoutSeconds": 15
                }
            }
        }
        """;

        var result = ConfigMigrator.MigrateLegacyKeys(json, Logger);
        var root = JsonNode.Parse(result)!;
        var bw = root["secrets"]!["bitwarden"]!;

        bw["timeout"].ShouldNotBeNull();
        bw["timeout"]!.GetValue<string>().ShouldBe(TimeSpan.FromSeconds(15).ToString());
        bw["timeoutSeconds"].ShouldBeNull();
    }

    // -- New name already present → no rename --

    [Test]
    public void MigrateLegacyKeys_NewNameAlreadyPresent_DoesNotOverwrite()
    {
        var json = """
        {
            "agents": {
                "defaults": {
                    "rateLimitWindowSeconds": 60,
                    "rateLimitWindow": "00:02:00"
                }
            }
        }
        """;

        var result = ConfigMigrator.MigrateLegacyKeys(json, Logger);
        var root = JsonNode.Parse(result)!;
        var defaults = root["agents"]!["defaults"]!;

        // The new value takes precedence — old value is NOT renamed over it.
        defaults["rateLimitWindow"]!.GetValue<string>().ShouldBe("00:02:00");
        // Old key is NOT removed since the new key already exists.
        defaults["rateLimitWindowSeconds"].ShouldNotBeNull();
    }

    // -- No legacy keys → JSON unchanged --

    [Test]
    public void MigrateLegacyKeys_NoLegacyKeys_ReturnsOriginalJson()
    {
        var json = """
        {
            "agents": {
                "defaults": {
                    "rateLimitWindow": "00:01:00",
                    "provider": "openai"
                }
            }
        }
        """;

        var result = ConfigMigrator.MigrateLegacyKeys(json, Logger);
        // Should return the original string unmodified (no re-serialization).
        result.ShouldBe(json);
    }

    // -- Missing parent paths → no crash --

    [Test]
    public void MigrateLegacyKeys_MissingParentPath_DoesNotThrow()
    {
        var json = """{"agents": {}}""";
        var result = ConfigMigrator.MigrateLegacyKeys(json, Logger);
        result.ShouldBe(json);
    }

    [Test]
    public void MigrateLegacyKeys_EmptyConfig_DoesNotThrow()
    {
        var json = """{}""";
        var result = ConfigMigrator.MigrateLegacyKeys(json, Logger);
        result.ShouldBe(json);
    }

    // -- Invalid JSON → returns original --

    [Test]
    public void MigrateLegacyKeys_InvalidJson_ReturnsOriginal()
    {
        var json = "not valid json {{{";
        var result = ConfigMigrator.MigrateLegacyKeys(json, Logger);
        result.ShouldBe(json);
    }

    // -- Multiple legacy keys at once --

    [Test]
    public void MigrateLegacyKeys_MultipleLegacyKeys_AllRenamed()
    {
        var json = """
        {
            "agents": {
                "defaults": {
                    "rateLimitWindowSeconds": 60,
                    "resilience": {
                        "requestTimeoutSeconds": 120,
                        "retry": {
                            "delaySeconds": 2.0
                        }
                    }
                }
            },
            "secrets": {
                "onePassword": {
                    "timeoutSeconds": 10
                },
                "bitwarden": {
                    "timeoutSeconds": 15
                }
            }
        }
        """;

        var result = ConfigMigrator.MigrateLegacyKeys(json, Logger);
        var root = JsonNode.Parse(result)!;

        root["agents"]!["defaults"]!["rateLimitWindow"].ShouldNotBeNull();
        root["agents"]!["defaults"]!["rateLimitWindowSeconds"].ShouldBeNull();

        root["agents"]!["defaults"]!["resilience"]!["requestTimeout"].ShouldNotBeNull();
        root["agents"]!["defaults"]!["resilience"]!["requestTimeoutSeconds"].ShouldBeNull();

        root["agents"]!["defaults"]!["resilience"]!["retry"]!["delay"].ShouldNotBeNull();
        root["agents"]!["defaults"]!["resilience"]!["retry"]!["delaySeconds"].ShouldBeNull();

        root["secrets"]!["onePassword"]!["timeout"].ShouldNotBeNull();
        root["secrets"]!["onePassword"]!["timeoutSeconds"].ShouldBeNull();

        root["secrets"]!["bitwarden"]!["timeout"].ShouldNotBeNull();
        root["secrets"]!["bitwarden"]!["timeoutSeconds"].ShouldBeNull();
    }

    // -- End-to-end: old config deserializes into AppConfig correctly --

    [Test]
    public void MigrateLegacyKeys_OldConfigFormat_DeserializesToCorrectValues()
    {
        var json = """
        {
            "agents": {
                "defaults": {
                    "provider": "openai",
                    "model": "gpt-4o",
                    "rateLimitWindowSeconds": 90,
                    "healthCheck": {
                        "intervalSeconds": 600
                    },
                    "resilience": {
                        "requestTimeoutSeconds": 180,
                        "retry": {
                            "delaySeconds": 2.5,
                            "maxDelaySeconds": 45
                        },
                        "circuitBreaker": {
                            "breakDurationSeconds": 60,
                            "samplingDurationSeconds": 120
                        }
                    }
                }
            },
            "secrets": {
                "onePassword": {
                    "timeoutSeconds": 20
                },
                "bitwarden": {
                    "timeoutSeconds": 25
                }
            }
        }
        """;

        var migrated = ConfigMigrator.MigrateLegacyKeys(json, Logger);
        var config = JsonSerializer.Deserialize(migrated, ConfigJsonContext.Default.AppConfig);

        config.ShouldNotBeNull();

        // Numeric seconds are converted to TimeSpan strings by ConfigMigrator, then parsed by TimeSpanSecondsConverter.
        config.Agents!.Defaults.RateLimitWindow.ShouldBe(TimeSpan.FromSeconds(90));
        config.Agents.Defaults.HealthCheck!.Interval.ShouldBe(TimeSpan.FromSeconds(600));
        config.Agents.Defaults.Resilience!.RequestTimeout.ShouldBe(TimeSpan.FromSeconds(180));
        config.Agents.Defaults.Resilience.Retry.Delay.ShouldBe(TimeSpan.FromSeconds(2.5));
        config.Agents.Defaults.Resilience.Retry.MaxDelay.ShouldBe(TimeSpan.FromSeconds(45));
        config.Agents.Defaults.Resilience.CircuitBreaker!.BreakDuration.ShouldBe(TimeSpan.FromSeconds(60));
        config.Agents.Defaults.Resilience.CircuitBreaker.SamplingDuration.ShouldBe(TimeSpan.FromSeconds(120));
        config.Secrets!.OnePassword!.Timeout.ShouldBe(TimeSpan.FromSeconds(20));
        config.Secrets.Bitwarden!.Timeout.ShouldBe(TimeSpan.FromSeconds(25));
    }

    // -- New config format still works --

    [Test]
    public void MigrateLegacyKeys_NewConfigFormat_DeserializesToCorrectValues()
    {
        var json = """
        {
            "agents": {
                "defaults": {
                    "provider": "openai",
                    "model": "gpt-4o",
                    "rateLimitWindow": "00:01:30",
                    "healthCheck": {
                        "interval": "00:10:00"
                    },
                    "resilience": {
                        "requestTimeout": "00:03:00",
                        "retry": {
                            "delay": "00:00:02.500",
                            "maxDelay": "00:00:45"
                        },
                        "circuitBreaker": {
                            "breakDuration": "00:01:00",
                            "samplingDuration": "00:02:00"
                        }
                    }
                }
            },
            "secrets": {
                "onePassword": {
                    "timeout": "00:00:20"
                },
                "bitwarden": {
                    "timeout": "00:00:25"
                }
            }
        }
        """;

        var migrated = ConfigMigrator.MigrateLegacyKeys(json, Logger);
        var config = JsonSerializer.Deserialize(migrated, ConfigJsonContext.Default.AppConfig);

        config.ShouldNotBeNull();

        config.Agents!.Defaults.RateLimitWindow.ShouldBe(TimeSpan.FromSeconds(90));
        config.Agents.Defaults.HealthCheck!.Interval.ShouldBe(TimeSpan.FromMinutes(10));
        config.Agents.Defaults.Resilience!.RequestTimeout.ShouldBe(TimeSpan.FromMinutes(3));
        config.Agents.Defaults.Resilience.Retry.Delay.ShouldBe(TimeSpan.FromSeconds(2.5));
        config.Agents.Defaults.Resilience.Retry.MaxDelay.ShouldBe(TimeSpan.FromSeconds(45));
        config.Agents.Defaults.Resilience.CircuitBreaker!.BreakDuration.ShouldBe(TimeSpan.FromMinutes(1));
        config.Agents.Defaults.Resilience.CircuitBreaker.SamplingDuration.ShouldBe(TimeSpan.FromMinutes(2));
        config.Secrets!.OnePassword!.Timeout.ShouldBe(TimeSpan.FromSeconds(20));
        config.Secrets.Bitwarden!.Timeout.ShouldBe(TimeSpan.FromSeconds(25));
    }
}
