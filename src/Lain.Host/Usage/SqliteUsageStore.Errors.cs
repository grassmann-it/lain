using Lain.Core;
using Microsoft.Data.Sqlite;

namespace Lain.Host.Usage;

public sealed partial class SqliteUsageStore
{
    private static readonly HashSet<string> ErrorCategories =
    [
        "invalid_request", "authentication", "permission", "rate_limited", "upstream_error",
        "canceled", "unknown", "unsupported_tool", "tool_limit"
    ];

    private static void ValidateErrorCategory(JournalEvent item)
    {
        if (item.ErrorCategory is null) return;
        if (item.Kind == "attempt_started" || !ErrorCategories.Contains(item.ErrorCategory))
            throw new InvalidDataException("Attempt errors require a fixed terminal category.");
    }

    // Only a qualified dispatch hook may assert these two categories. A generic
    // HTTP 400 cannot establish a tool capability or limit violation.
    private static bool DispatchError(string category) => category is "unsupported_tool" or "tool_limit";

    private static AttemptErrorDto? DescribeError(string? reportedCategory, int? status, string state, string? completion)
    {
        var effectiveState = completion ?? state;
        var outcomeEvidence = completion is null ? "reported_outcome" : "completion_state";
        var (category, evidence) = effectiveState == "canceled" ? ("canceled", outcomeEvidence)
            : reportedCategory is not null && DispatchError(reportedCategory) ? (reportedCategory, "dispatch_validation")
            : status switch
            {
                400 => ("invalid_request", "http_status"),
                401 => ("authentication", "http_status"),
                403 => ("permission", "http_status"),
                429 => ("rate_limited", "http_status"),
                >= 500 => ("upstream_error", "http_status"),
                >= 400 => ("unknown", "http_status"),
                _ when effectiveState == "failed" => ("unknown", outcomeEvidence),
                _ => ((string?)null, (string?)null)
            };
        if (category is null) return null;
        var action = category switch
        {
            "invalid_request" => "check_request",
            "authentication" => "reconnect_account",
            "permission" => "check_permissions",
            "rate_limited" or "upstream_error" => "retry_later",
            "canceled" => "retry_if_needed",
            "unsupported_tool" => "change_tool_configuration",
            "tool_limit" => "reduce_tool_count",
            _ => "inspect_attempt"
        };
        return new(category, evidence!, action);
    }

    private static async Task SaveErrorCategoryAsync(SqliteConnection connection, SqliteTransaction transaction, JournalEvent item, CancellationToken ct)
    {
        if (item.ErrorCategory is null) return;
        using var read = Command(connection, transaction,
            "SELECT error_category,status_code,state,completion_state FROM attempts WHERE source_id=$source AND attempt_id=$attempt",
            ("$source", item.SourceId), ("$attempt", item.AttemptId));
        using var reader = await read.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidDataException("Attempt errors require an existing attempt.");
        var previous = reader.IsDBNull(0) ? null : reader.GetString(0);
        int? status = reader.IsDBNull(1) ? null : reader.GetInt32(1);
        var state = reader.GetString(2);
        var completion = reader.IsDBNull(3) ? null : reader.GetString(3);
        if (previous is not null && previous != item.ErrorCategory)
            throw new InvalidDataException("Observed attempt error changed.");
        if (DispatchError(item.ErrorCategory))
        {
            if ((completion ?? state) != "failed")
                throw new InvalidDataException("Dispatch validation errors require a failed attempt.");
        }
        else if (DescribeError(null, status, state, completion)?.Category != item.ErrorCategory)
            throw new InvalidDataException("Attempt error category contradicts structured status or outcome.");
        reader.Close();
        using var update = Command(connection, transaction,
            "UPDATE attempts SET error_category=$category WHERE source_id=$source AND attempt_id=$attempt",
            ("$category", item.ErrorCategory), ("$source", item.SourceId), ("$attempt", item.AttemptId));
        await update.ExecuteNonQueryAsync(ct);
    }
}
