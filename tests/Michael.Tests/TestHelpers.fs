module Michael.Tests.TestHelpers

open System
open System.Security.Cryptography

/// Generate a fake cancellation token matching the production format
/// (64-character uppercase hex string from 32 random bytes).
let makeFakeCancellationToken () =
    Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
