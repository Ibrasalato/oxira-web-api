using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.RegularExpressions;
using Oxira.Web.Domain;

namespace Oxira.Web.Tests;

public class AuthTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task Signup_verify_refresh_rotation_and_reuse_detection()
    {
        var c = app.Client();
        var (s, b) = await c.PostJson("/api/auth/signup", new { email = "Sara@Example.com", name = "سارة", password = "كلمة123سر", lang = "ar" });
        Assert.Equal(HttpStatusCode.OK, s);
        Assert.True(b.GetProperty("success").GetBoolean());

        // wrong code first: counted, then the right one signs in
        var right = app.CodeFor("sara@example.com");
        var (s1, wrong) = await c.PostJson("/api/auth/verify", new { email = "sara@example.com", code = right == "000000" ? "111111" : "000000", purpose = "verify" });
        Assert.Equal(HttpStatusCode.BadRequest, s1);
        Assert.Equal("code", wrong.Str("reason"));
        var (s2, auth) = await c.PostJson("/api/auth/verify", new { email = "sara@example.com", code = right, purpose = "verify" });
        Assert.Equal(HttpStatusCode.OK, s2);
        var token = auth.Str("token");
        var refresh = auth.Str("refreshToken");
        Assert.True(auth.TryGetProperty("expiresAt", out _) && auth.TryGetProperty("refreshExpiresAt", out _));
        Assert.Equal("sara@example.com", auth.GetProperty("user").Str("email"));

        // the token carries the unified Oxira claims
        var jwt = new JwtSecurityTokenHandler { MapInboundClaims = false }.ReadJwtToken(token);
        Assert.Equal("https://api.oxira.sa", jwt.Issuer);
        Assert.Contains("oxira", jwt.Audiences);
        foreach (var claim in new[] { "sub", "email", "name", "role", "stamp", "jti", "iat", "sid" }) Assert.Contains(jwt.Claims, x => x.Type == claim);
        Assert.Equal("customer", jwt.Claims.First(x => x.Type == "role").Value);
        Assert.True(jwt.ValidTo - jwt.ValidFrom <= TimeSpan.FromMinutes(15));

        var (sm, me) = await app.Client(token).GetJson("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, sm);
        Assert.Equal("سارة", me.Str("name"));
        Assert.Equal("owner", me.Str("role"));
        Assert.True(me.GetProperty("has_password").GetBoolean());

        // refresh rotates; presenting the old token again ends the whole sign-in
        var (sr, next) = await c.PostJson("/api/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.OK, sr);
        Assert.NotEqual(refresh, next.Str("refreshToken"));
        var (reuse, rb) = await c.PostJson("/api/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse);
        Assert.Equal("session", rb.Str("reason"));
        var (after, _) = await c.PostJson("/api/auth/refresh", new { refreshToken = next.Str("refreshToken") });
        Assert.Equal(HttpStatusCode.Unauthorized, after);

        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client().GetJson("/api/auth/me")).Status);
    }

    [Fact]
    public async Task Login_reasons_lockout_and_two_step()
    {
        var c = app.Client();
        await c.PostJson("/api/auth/signup", new { email = "late@example.com", name = "Late", password = "Secret123" });
        // not verified yet: the login sends a code and says so
        var (_, unverified) = await c.PostJson("/api/auth/login", new { email = "late@example.com", password = "Secret123" });
        Assert.False(unverified.GetProperty("success").GetBoolean());
        Assert.Equal("unverified", unverified.Str("reason"));

        var (weak, wb) = await c.PostJson("/api/auth/signup", new { email = "x@example.com", name = "X", password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, weak);
        Assert.Equal("weak", wb.Str("reason"));
        await app.SignUpAsync("dup@example.com");
        var (dup, db) = await c.PostJson("/api/auth/signup", new { email = "dup@example.com", name = "Dup", password = "Secret123" });
        Assert.Equal(HttpStatusCode.Conflict, dup);
        Assert.Equal("exists", db.Str("reason"));

        await app.SignUpAsync("lock@example.com");
        for (var i = 0; i < 4; i++)
        {
            var (_, bad) = await c.PostJson("/api/auth/login", new { email = "lock@example.com", password = "Wrong1234" });
            Assert.Equal("invalid", bad.Str("reason"));
        }
        var (ls, locked) = await c.PostJson("/api/auth/login", new { email = "lock@example.com", password = "Wrong1234" });
        Assert.Equal(HttpStatusCode.TooManyRequests, ls);
        Assert.Equal("locked", locked.Str("reason"));
        Assert.Equal(15, locked.GetProperty("minutes").GetInt32());
        var (_, still) = await c.PostJson("/api/auth/login", new { email = "lock@example.com", password = "Secret123" });
        Assert.Equal("locked", still.Str("reason"));
        app.Clock.Advance(TimeSpan.FromMinutes(16));
        var (ok, _) = await c.PostJson("/api/auth/login", new { email = "lock@example.com", password = "Secret123" });
        Assert.Equal(HttpStatusCode.OK, ok);

        // two-step sign-in, sent as a form (the website posts URLSearchParams)
        var token = (await app.SignUpAsync("twofa@example.com")).Str("token");
        var (_, on) = await app.Client(token).PostForm("/api/auth/twofa", new() { ["current"] = "Secret123", ["on"] = "1" });
        Assert.True(on.GetProperty("twofa").GetBoolean());
        app.Clock.Advance(TimeSpan.FromMinutes(2));
        var (_, step) = await c.PostForm("/api/auth/login", new() { ["email"] = "twofa@example.com", ["password"] = "Secret123", ["lang"] = "en" });
        Assert.Equal("twofa", step.Str("reason"));
        var (vs, signed) = await c.PostForm("/api/auth/verify", new() { ["email"] = "twofa@example.com", ["code"] = app.CodeFor("twofa@example.com"), ["purpose"] = "login" });
        Assert.Equal(HttpStatusCode.OK, vs);
        Assert.False(string.IsNullOrEmpty(signed.Str("token")));

        // resending too soon says how long to wait
        var (ws, wait) = await c.PostJson("/api/auth/resend", new { email = "twofa@example.com", purpose = "login" });
        Assert.Equal(HttpStatusCode.TooManyRequests, ws);
        Assert.Equal("wait", wait.Str("reason"));
        Assert.True(wait.GetProperty("seconds").GetInt32() > 0);
    }

    [Fact]
    public async Task Reset_and_password_change_end_other_sessions()
    {
        var first = await app.SignUpAsync("reset@example.com");
        var c = app.Client();
        var (_, f) = await c.PostJson("/api/auth/forgot", new { email = "reset@example.com" });
        Assert.True(f.GetProperty("success").GetBoolean());
        var (_, unknown) = await c.PostJson("/api/auth/forgot", new { email = "nobody@example.com" });
        Assert.True(unknown.GetProperty("success").GetBoolean());
        var (rs, reset) = await c.PostJson("/api/auth/reset", new { email = "reset@example.com", code = app.CodeFor("reset@example.com"), password = "NewPass456" });
        Assert.Equal(HttpStatusCode.OK, rs);
        // the old access token (old security stamp) and refresh token stop working
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client(first.Str("token")).GetJson("/api/auth/me")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostJson("/api/auth/refresh", new { refreshToken = first.Str("refreshToken") })).Status);

        var token = reset.Str("token");
        var (bad, bb) = await app.Client(token).PostJson("/api/auth/password", new { current = "nope", password = "Other789x" });
        Assert.Equal(HttpStatusCode.BadRequest, bad);
        Assert.Equal("invalid", bb.Str("reason"));
        var (cs, changed) = await app.Client(token).PostJson("/api/auth/password", new { current = "NewPass456", password = "Other789x" });
        Assert.Equal(HttpStatusCode.OK, cs);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client(token).GetJson("/api/auth/me")).Status);
        Assert.Equal(HttpStatusCode.OK, (await app.Client(changed.Str("token")).GetJson("/api/auth/me")).Status);
        Assert.Contains(app.Email.To("reset@example.com"), m => m.Subject.Contains("كلمة المرور"));
    }

    [Fact]
    public async Task Magic_link_signs_in_once()
    {
        var c = app.Client();
        var (s, b) = await c.PostJson("/api/auth/magic-link", new { email = "new.client@example.com", lang = "en", returnUrl = "https://design.oxira.sa/order/?step=2#top" });
        Assert.Equal(HttpStatusCode.OK, s);
        Assert.True(b.GetProperty("success").GetBoolean());
        var mail = app.Email.To("new.client@example.com").Single();
        var m = Regex.Match(mail.Html, @"https://design\.oxira\.sa/order/\?step=2&amp;k=([a-f0-9]{40})#top");
        Assert.True(m.Success, mail.Html);
        var code = m.Groups[1].Value;

        // a second request within two minutes sends nothing (still success)
        var (_, again) = await c.PostJson("/api/auth/magic-link", new { email = "new.client@example.com", lang = "en", returnUrl = "https://design.oxira.sa/" });
        Assert.True(again.GetProperty("success").GetBoolean());
        Assert.Single(app.Email.To("new.client@example.com"));

        var (cs, auth) = await c.PostJson("/api/auth/magic-link/complete", new { code });
        Assert.Equal(HttpStatusCode.OK, cs);
        Assert.Equal("new.client@example.com", auth.GetProperty("user").Str("email"));
        var (_, me) = await app.Client(auth.Str("token")).GetJson("/api/auth/me");
        Assert.False(me.GetProperty("has_password").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostJson("/api/auth/magic-link/complete", new { code })).Status);

        // only Oxira origins (and not look-alikes)
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostJson("/api/auth/magic-link", new { email = "a@example.com", returnUrl = "https://design.oxira.sa.evil.com/" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostJson("/api/auth/magic-link", new { email = "a@example.com", returnUrl = "https://evil.com/?https://oxira.sa" })).Status);

        // expired after 30 minutes
        await c.PostJson("/api/auth/magic-link", new { email = "slow@example.com", returnUrl = "https://oxira.sa/account/" });
        var slow = Regex.Match(app.Email.To("slow@example.com").Single().Html, @"k=([a-f0-9]{40})").Groups[1].Value;
        app.Clock.Advance(TimeSpan.FromMinutes(31));
        var (es, eb) = await c.PostJson("/api/auth/magic-link/complete", new { code = slow });
        Assert.Equal(HttpStatusCode.BadRequest, es);
        Assert.Equal("expired", eb.Str("reason"));
    }

    [Fact]
    public async Task Team_switch_sessions_and_role_changes()
    {
        var owner = await app.SignUpAsync("owner@example.com", name: "Owner Co");
        var member = await app.SignUpAsync("member@example.com");
        var ownerId = owner.GetProperty("user").GetProperty("id").GetGuid();
        var memberId = member.GetProperty("user").GetProperty("id").GetGuid();
        await app.Db(async db =>
        {
            db.TeamMembers.Add(new TeamMember { OwnerId = ownerId, Email = "member@example.com", MemberUserId = memberId, Role = MemberRoles.Viewer, CreatedAt = DateTimeOffset.UtcNow, JoinedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });

        var mc = app.Client(member.Str("token"));
        var (_, me) = await mc.GetJson("/api/auth/me");
        Assert.Equal("owner@example.com", me.GetProperty("memberships")[0].Str("owner"));
        var (ss, switched) = await mc.PostJson("/api/auth/switch", new { owner = "owner@example.com" });
        Assert.Equal(HttpStatusCode.OK, ss);
        var acting = app.Client(switched.Str("token"));
        var (_, me2) = await acting.GetJson("/api/auth/me");
        Assert.Equal("owner@example.com", me2.Str("acting_as"));
        Assert.Equal("viewer", me2.Str("role"));
        // the previous session of this device ended
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client().PostJson("/api/auth/refresh", new { refreshToken = member.Str("refreshToken") })).Status);
        // a refresh keeps working on the owner's account
        var (_, refreshed) = await app.Client().PostJson("/api/auth/refresh", new { refreshToken = switched.Str("refreshToken") });
        var (_, me3) = await app.Client(refreshed.Str("token")).GetJson("/api/auth/me");
        Assert.Equal("owner@example.com", me3.Str("acting_as"));
        Assert.Equal(HttpStatusCode.Forbidden, (await mc.PostJson("/api/auth/switch", new { owner = "stranger@example.com" })).Status);

        // devices: sign out every device, this one gets a new session
        var oc = app.Client(owner.Str("token"));
        await app.Client().PostJson("/api/auth/login", new { email = "owner@example.com", password = "Secret123" });
        var (_, list) = await oc.GetJson("/api/auth/sessions");
        var sessions = list.GetProperty("sessions").EnumerateArray().ToList();
        Assert.True(sessions.Count >= 2);
        Assert.Single(sessions, x => x.GetProperty("current").GetBoolean());
        var (ra, fresh) = await oc.PostJson("/api/auth/sessions/revoke-all", new { });
        Assert.Equal(HttpStatusCode.OK, ra);
        var (_, list2) = await app.Client(fresh.Str("token")).GetJson("/api/auth/sessions");
        Assert.Single(list2.GetProperty("sessions").EnumerateArray());

        // an admin makes someone staff: their old token stops at once
        var boss = await app.TokenAsync("boss@oxira.sa");
        var (rs, role) = await app.Client(boss).PutJson($"/api/admin/users/{memberId}/role", new { role = "staff" });
        Assert.Equal(HttpStatusCode.OK, rs);
        Assert.Equal("staff", role.Str("role"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await acting.GetJson("/api/auth/me")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Client(fresh.Str("token")).GetJson("/api/admin/users")).Status);
    }

    [Fact]
    public async Task Profile_and_signout()
    {
        var s = await app.SignUpAsync("profile@example.com");
        var c = app.Client(s.Str("token"));
        var (ps, p) = await c.PutJson("/api/auth/profile", new { name = "New Name", phone = "0555555555", company = "Acme", avatar = "i_0123456789abcdef0123_j" });
        Assert.Equal(HttpStatusCode.OK, ps);
        Assert.Equal("i_0123456789abcdef0123_j", p.Str("avatar"));
        var (bad, bb) = await c.PutJson("/api/auth/profile", new { name = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, bad);
        Assert.Equal("name", bb.Str("reason"));
        var (os, _) = await c.PostJson("/api/auth/signout", new { refreshToken = s.Str("refreshToken") });
        Assert.Equal(HttpStatusCode.OK, os);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client().PostJson("/api/auth/refresh", new { refreshToken = s.Str("refreshToken") })).Status);
    }
}
