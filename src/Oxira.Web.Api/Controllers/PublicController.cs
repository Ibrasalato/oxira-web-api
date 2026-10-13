using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Oxira.Web.Application.Features.Agent;
using Oxira.Web.Application.Features.Audit;
using Oxira.Web.Application.Features.Chat;
using Oxira.Web.Application.Features.Public;

namespace Oxira.Web.Api.Controllers;

/// <summary>Public forms on oxira.sa (JSON, text/plain JSON or form-urlencoded; the hidden company_website field is a bot trap).</summary>
[ApiController, Route("api")]
public class PublicController(ISender mediator) : ControllerBase
{
    /// <summary>Contact form: type project | support. Emails the team and the visitor.</summary>
    [HttpPost("contact"), EnableRateLimiting("forms")]
    public Task<SubmitResult> Contact(ContactInput body, CancellationToken ct) => mediator.Send(new ContactCommand(body), ct);

    /// <summary>Partner programme sign-up: emails the partner their referral link (?ref=code).</summary>
    [HttpPost("partners"), EnableRateLimiting("forms")]
    public Task<SubmitResult> Partner(PartnerInput body, CancellationToken ct) => mediator.Send(new PartnerSignupCommand(body), ct);

    /// <summary>A client review (rating 1–5, text 15–800, consent). Shown after the team approves it.</summary>
    [HttpPost("testimonials"), EnableRateLimiting("forms")]
    public Task<SubmitResult> Testimonial(TestimonialInput body, CancellationToken ct) => mediator.Send(new TestimonialCommand(body), ct);

    /// <summary>Social proof: published sites and approved reviews.</summary>
    [HttpGet("proof"), ResponseCache(Duration = 300)]
    public Task<ProofDto> Proof(CancellationToken ct) => mediator.Send(new ProofQuery(), ct);

    /// <summary>The oxira.sa AI assistant. Saves a lead when the visitor confirms a request.</summary>
    [HttpPost("chat"), EnableRateLimiting("chat")]
    public Task<ChatReply> Chat(ChatInput body, CancellationToken ct) => mediator.Send(new SendChatCommand(body), ct);

    /// <summary>The visible history of a chat session.</summary>
    [HttpGet("chat/{sessionId}")]
    public Task<List<ChatTurnDto>> ChatHistory(string sessionId, CancellationToken ct) => mediator.Send(new GetChatQuery(sessionId), ct);

    /// <summary>Free website check: fetches the site (public addresses only) and scores 14 checks.</summary>
    [HttpPost("audit"), EnableRateLimiting("ai")]
    public Task<AuditResult> Audit(AuditInput body, CancellationToken ct) => mediator.Send(new RunAuditCommand(body), ct);

    /// <summary>Build a WhatsApp agent demo from a website and/or notes. Errors (error): url, email, input, limit, fetch, content, server.</summary>
    [HttpPost("agent/demos"), EnableRateLimiting("ai")]
    public Task<TryAgentResult> TryAgent(TryAgentInput body, CancellationToken ct) => mediator.Send(new TryAgentCommand(body), ct);

    /// <summary>A demo's business, welcome message and suggested questions.</summary>
    [HttpGet("agent/demos/{id}")]
    public Task<DemoDto> Demo(string id, CancellationToken ct) => mediator.Send(new GetDemoQuery(id), ct);

    /// <summary>Same as GET agent/demos/{id}, with the page's ?d= parameter.</summary>
    [HttpGet("agent/demo"), ApiExplorerSettings(IgnoreApi = true)]
    public Task<DemoDto> DemoByQuery([FromQuery] string? d, CancellationToken ct) => mediator.Send(new GetDemoQuery(d), ct);

    /// <summary>One message to a demo agent (history: the last 10 turns). left = messages remaining.</summary>
    [HttpPost("agent/chat"), EnableRateLimiting("chat")]
    public Task<DemoReply> DemoChat(DemoChatInput body, CancellationToken ct) => mediator.Send(new DemoChatCommand(body), ct);

    /// <summary>Subscribe to the WhatsApp agent (plan basic | pro). The team follows up.</summary>
    [HttpPost("agent/subscriptions"), EnableRateLimiting("forms")]
    public Task<SubmitResult> AgentSubscribe(AgentSubscribeInput body, CancellationToken ct) => mediator.Send(new AgentSubscribeCommand(body), ct);
}

/// <summary>Website leads and reviews (Oxira team).</summary>
[ApiController, Route("api/admin"), Authorize(Policy = "staff")]
public class LeadsAdminController(ISender mediator) : ControllerBase
{
    [HttpGet("leads")]
    public Task<List<LeadDto>> Leads([FromQuery] string? kind, [FromQuery] int take = 100, CancellationToken ct = default) => mediator.Send(new ListLeadsQuery(kind, take), ct);

    [HttpGet("testimonials")]
    public Task<List<TestimonialDto>> Testimonials([FromQuery] string? status, CancellationToken ct) => mediator.Send(new ListTestimonialsQuery(status), ct);

    /// <summary>approved | rejected | pending</summary>
    [HttpPut("testimonials/{id:int}")]
    public Task<TestimonialDto> Moderate(int id, StatusInput body, CancellationToken ct) => mediator.Send(new ModerateTestimonialCommand(id, body.Status), ct);
}
