namespace PoultryFarm.Api.Services;

public static class FarmRegistrationEmailComposer
{
    public static (string Subject, string Html, string Text) BuildApprovalInvite(
        string contactFirstName,
        string farmName,
        string username,
        string inviteUrl,
        DateTimeOffset inviteExpiresAt)
    {
        var firstName = string.IsNullOrWhiteSpace(contactFirstName) ? "there" : contactFirstName.Trim();
        var farm = string.IsNullOrWhiteSpace(farmName) ? "your farm" : farmName.Trim();
        var expiresLocal = inviteExpiresAt.ToLocalTime().ToString("f");

        var subject = $"You're approved — set up your {farm} farm account";
        var text =
            $"""
            Hi {firstName},

            Good news — your Poultry Zone farm registration for {farm} has been approved.

            Username: {username}

            Use this invitation link to set your password and sign in to the farm dashboard:
            {inviteUrl}

            This link expires on {expiresLocal}.

            If you did not apply for marketplace access, you can ignore this email.

            — Poultry Zone
            """;

        var html =
            $"""
            <div style="font-family:Segoe UI,Helvetica,Arial,sans-serif;line-height:1.55;color:#0f172a;max-width:560px;margin:0 auto;padding:24px;">
              <h1 style="font-size:22px;margin:0 0 12px;">Your farm application was approved</h1>
              <p style="margin:0 0 12px;">Hi {System.Net.WebUtility.HtmlEncode(firstName)},</p>
              <p style="margin:0 0 12px;">
                Your Poultry Zone registration for
                <strong>{System.Net.WebUtility.HtmlEncode(farm)}</strong>
                has been approved. You can now set your password and sign in to the farm dashboard.
              </p>
              <p style="margin:0 0 16px;">
                <strong>Username:</strong> {System.Net.WebUtility.HtmlEncode(username)}
              </p>
              <p style="margin:0 0 20px;">
                <a href="{System.Net.WebUtility.HtmlEncode(inviteUrl)}"
                   style="display:inline-block;background:#166534;color:#fff;text-decoration:none;padding:12px 18px;border-radius:10px;font-weight:700;">
                  Set your password
                </a>
              </p>
              <p style="margin:0 0 8px;font-size:14px;color:#475569;">
                Or paste this link into your browser:<br />
                <a href="{System.Net.WebUtility.HtmlEncode(inviteUrl)}" style="color:#166534;word-break:break-all;">
                  {System.Net.WebUtility.HtmlEncode(inviteUrl)}
                </a>
              </p>
              <p style="margin:16px 0 0;font-size:13px;color:#64748b;">
                This invitation expires on {System.Net.WebUtility.HtmlEncode(expiresLocal)}.
              </p>
              <p style="margin:18px 0 0;font-size:13px;color:#64748b;">— Poultry Zone</p>
            </div>
            """;

        return (subject, html, text);
    }
}
