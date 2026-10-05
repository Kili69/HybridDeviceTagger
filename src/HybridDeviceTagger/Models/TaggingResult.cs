/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

namespace HybridDeviceTagger.Models;

/// <summary>Summarizes one idempotent device-tagging run.</summary>
/// <param name="Discovered">Number of AD computers discovered.</param>
/// <param name="Updated">Number of Entra devices updated.</param>
/// <param name="Unchanged">Number already carrying the configured value.</param>
/// <param name="NotFound">Number without a matching Entra device.</param>
/// <param name="NotHybridJoined">Number whose Entra trust type was not <c>ServerAd</c>.</param>
/// <param name="MembershipsAdded">Number of Entra memberships added.</param>
/// <param name="MembershipsRemoved">Number of owned Entra memberships removed.</param>
/// <param name="RemovedFromScope">Number of managed devices no longer found in any configured OU.</param>
public sealed record TaggingResult(
    int Discovered,
    int Updated,
    int Unchanged,
    int NotFound,
    int NotHybridJoined,
    int MembershipsAdded = 0,
    int MembershipsRemoved = 0,
    int RemovedFromScope = 0);