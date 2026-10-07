using ArvindJobHunter.Agents.Llm;
using ArvindJobHunter.Agents.Tools;
using ArvindJobHunter.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace ArvindJobHunter.Agents;

public static class DependencyInjection
{
    public static IServiceCollection AddAgents(this IServiceCollection services, OpenAiOptions openAi)
    {
        services.AddSingleton(openAi);
        services.AddSingleton<ILlmProvider, DemoLlmProvider>();
        services.AddHttpClient<OpenAiLlmProvider>();
        services.AddSingleton<ILlmProvider>(sp => sp.GetRequiredService<OpenAiLlmProvider>());
        services.AddSingleton<ILlmProviderResolver, LlmProviderResolver>();

        services.AddSingleton<IAgentTool, AnalyzeJobTool>();
        services.AddSingleton<IAgentTool, MatchJobTool>();
        services.AddSingleton<IAgentTool, ReadResumeTool>();
        services.AddSingleton<IAgentTool, CustomizeResumeTool>();
        services.AddSingleton<IAgentTool, GenerateCoverLetterTool>();
        services.AddSingleton<IAgentTool, SendGmailEmailTool>();
        services.AddSingleton<IAgentTool, CreateGmailDraftTool>();
        services.AddSingleton<IAgentTool, SubmitApplicationTool>();
        services.AddSingleton<ToolRegistry>();

        services.AddScoped<AgentOrchestrator>();
        return services;
    }
}
