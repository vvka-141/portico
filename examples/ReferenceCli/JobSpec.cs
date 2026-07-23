using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Portico;

namespace ReferenceCli;

/// <summary>
/// A <see cref="CliOptions"/> bundle — the CLI analogue of <c>[FromBody] RequestDto</c>. When a
/// command has several options that travel together, declare them as one class and take it as a
/// single parameter; the framework materializes it from the command line. Use a bundle once a
/// command grows past three or so related options.
/// </summary>
/// <remarks>
/// <para>
/// A bundle is constructed per invocation via <c>Activator.CreateInstance</c>, so it needs a public
/// parameterless constructor (analyzer rule POR006). Each <c>[CliOption]</c> property binds exactly
/// as a method parameter would.
/// </para>
/// <para>
/// Implement <see cref="IValidatableObject"/> for <b>cross-property</b> rules — the ones a single
/// <c>[Range]</c> or <c>[Required]</c> on one property cannot express. The framework runs
/// <see cref="Validate"/> after binding and fails the command with exit code 2 if it returns any
/// error, so an invalid combination never reaches the handler.
/// </para>
/// </remarks>
public sealed class JobSpec : CliOptions, IValidatableObject
{
    /// <summary>The queue to submit into.</summary>
    [CliOption("--queue|-q", "Target queue")]
    [Required]
    public string Queue { get; set; } = "";

    // A bundle property is REQUIRED unless the attribute carries a DefaultValue — a C# property
    // initializer (`= 5`) does not make the option optional the way a method-parameter default does,
    // because the framework constructs the bundle and then sets only the properties the user passed.
    // DefaultValue is the string form of the default, parsed through the same converter.

    /// <summary>Scheduling priority; higher runs sooner.</summary>
    [CliOption("--priority|-p", "Priority 1-9 (higher runs first)", DefaultValue = "5")]
    [Range(1, 9)]
    public int Priority { get; set; } = 5;

    /// <summary>How many times to retry on failure before parking the job.</summary>
    [CliOption("--max-retries", "Retries before the job is parked", DefaultValue = "0")]
    [Range(0, 100)]
    public int MaxRetries { get; set; }

    /// <summary>Park a job that exhausts its retries instead of dropping it. A two-state option.</summary>
    [CliOption("--park-on-failure", "Keep an exhausted job for inspection", DefaultValue = "false")]
    public bool ParkOnFailure { get; set; }

    /// <summary>
    /// Cross-property rule: a job that is not parked on failure must not also demand retries it will
    /// silently discard. A per-property attribute cannot see two properties at once — this can.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!ParkOnFailure && MaxRetries > 0)
        {
            yield return new ValidationResult(
                "--max-retries has no effect without --park-on-failure: an unparked job is dropped on the " +
                "first failure. Add --park-on-failure, or set --max-retries 0.",
                [nameof(MaxRetries), nameof(ParkOnFailure)]);
        }
    }
}
