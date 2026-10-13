import { HelpArticle, HelpCategoryDefinition } from './help.models';

export const HELP_CATEGORIES: HelpCategoryDefinition[] = [
  { key: 'Getting Started', title: 'Getting Started', description: 'Core setup, startup, and first-use guidance.' },
  { key: 'Google OAuth', title: 'Google OAuth', description: 'Google Cloud setup, OAuth troubleshooting, and account connection guidance.' },
  { key: 'Gmail Integration', title: 'Gmail Integration', description: 'How Gmail is used by the application and what access is required.' },
  { key: 'Runtime & AI', title: 'Runtime & AI', description: 'Execution modes, LLM configuration, and runtime behavior.' },
  { key: 'Approvals', title: 'Approvals', description: 'Approval-first execution model and action control.' },
  { key: 'Data & Storage', title: 'Data & Storage', description: 'Local persistence, JSON storage, and data layout.' },
  { key: 'Deployment', title: 'Deployment', description: 'Production setup, secrets, environment configuration, and release guidance.' }
];

export const HELP_ARTICLES: HelpArticle[] = [
  {
    id: 'startup-overview',
    category: 'Getting Started',
    title: 'How do I start the full application?',
    summary: 'Build and run the backend and frontend together for local development.',
    keywords: ['start', 'run', 'build', 'backend', 'frontend', 'startup', 'local'],
    answer: 'Use the local startup workflow from the repository root so the backend and frontend are built before launch. The application uses a .NET backend and Angular frontend. Confirm the .NET 10 SDK and Node.js are installed, then follow the startup guide to build and start both services. The frontend runs on localhost:4200 and the API typically runs on localhost:5228.',
    references: [
      { title: 'Startup Guide', path: 'docs/STARTUP_GUIDE.md' },
      { title: 'Local Development', path: 'docs/local-development.md' }
    ]
  },
  {
    id: 'google-oauth-config',
    category: 'Google OAuth',
    title: 'How do I configure Google OAuth in the application?',
    summary: 'Enter the Google Client ID, Client Secret, and Redirect URI from Settings and save them to appsettings.json through the backend.',
    keywords: ['google', 'oauth', 'client id', 'client secret', 'redirect uri', 'settings', 'appsettings'],
    answer: 'Open Settings and go to the Google / Gmail section. Enter the Google Client ID, Client Secret, and Redirect URI, then save the settings. The backend writes these values into the API appsettings.json file through the existing settings endpoint. After saving, use Connect Google to complete the Google consent flow.',
    references: [
      { title: 'Google OAuth Configuration Guide', path: 'docs/GOOGLE_OAUTH_CONFIGURATION_GUIDE.md' },
      { title: 'Google OAuth User Flow', path: 'docs/GOOGLE_OAUTH_USER_FLOW.md' }
    ]
  },
  {
    id: 'google-cloud-setup',
    category: 'Google OAuth',
    title: 'What must be configured in Google Cloud Console?',
    summary: 'Enable Gmail API, configure Branding, Audience, Clients, and Data Access in Google Auth Platform.',
    keywords: ['google cloud', 'gmail api', 'branding', 'audience', 'clients', 'data access', 'test users'],
    answer: 'In Google Cloud Console, enable Gmail API first. Then configure the Google Auth Platform sections: Branding for app information, Audience with External and Testing plus your test users, Clients with the exact redirect URI, and Data Access with the Gmail and email scopes required by the app. The redirect URI must match exactly, including protocol, port, and path.',
    references: [
      { title: 'Google Cloud Setup', path: 'docs/GMAIL_GOOGLE_CLOUD_SETUP.md' },
      { title: 'Google OAuth Configuration Guide', path: 'docs/GOOGLE_OAUTH_CONFIGURATION_GUIDE.md' }
    ]
  },
  {
    id: 'google-invalid-client',
    category: 'Google OAuth',
    title: 'How do I fix invalid_client errors?',
    summary: 'Check that the Client ID and Client Secret are valid, matched, and loaded by the backend.',
    keywords: ['invalid_client', '401', 'client id', 'client secret', 'oauth'],
    answer: 'An invalid_client error usually means Google does not accept the OAuth client configuration. Confirm the Client ID and Client Secret belong to the same Google OAuth client, ensure they were saved correctly in the application, and restart the backend if configuration was changed. If necessary, regenerate the client secret in Google Cloud Console and save the updated value again.',
    references: [
      { title: 'Google OAuth 401 Fix', path: 'docs/GMAIL_OAUTH_401_FIX.md' },
      { title: 'Google OAuth Configuration Guide', path: 'docs/GOOGLE_OAUTH_CONFIGURATION_GUIDE.md' }
    ]
  },
  {
    id: 'google-redirect-uri',
    category: 'Google OAuth',
    title: 'How do I fix redirect_uri_mismatch?',
    summary: 'Register the exact callback URI in the Google OAuth client configuration.',
    keywords: ['redirect_uri_mismatch', 'redirect uri', 'callback', 'localhost:5228', 'localhost:7288'],
    answer: 'This error occurs when the redirect URI sent by the application does not exactly match an authorized redirect URI in Google Cloud Console. Add the exact callback URI used by the app, such as http://localhost:5228/api/integrations/google/callback, and also add the HTTPS localhost callback if your backend uses that profile. The values must match exactly without path or trailing-slash differences.',
    references: [
      { title: 'Google OAuth Configuration Guide', path: 'docs/GOOGLE_OAUTH_CONFIGURATION_GUIDE.md' },
      { title: 'Google Cloud Setup', path: 'docs/GMAIL_GOOGLE_CLOUD_SETUP.md' }
    ]
  },
  {
    id: 'google-access-denied',
    category: 'Google OAuth',
    title: 'Why do I see access_denied or app not verified?',
    summary: 'The app is in testing mode and your Google account must be added as a test user.',
    keywords: ['access_denied', 'not verified', 'test users', 'audience', 'google testing'],
    answer: 'When the Google OAuth app is still in Testing mode, only approved test users may use it. Add the target Google account under Audience > Test users in Google Auth Platform, save the change, wait a few minutes for it to propagate, and retry the connection flow.',
    references: [
      { title: 'Google OAuth Configuration Guide', path: 'docs/GOOGLE_OAUTH_CONFIGURATION_GUIDE.md' },
      { title: 'Google OAuth User Flow', path: 'docs/GOOGLE_OAUTH_USER_FLOW.md' }
    ]
  },
  {
    id: 'google-session-invalid',
    category: 'Google OAuth',
    title: 'Why did the Google sign-in session become invalid?',
    summary: 'The OAuth state must survive between the authorize and callback requests.',
    keywords: ['session expired', 'invalid state', 'oauth state', 'google authorization failed'],
    answer: 'This was caused by the OAuth service storing state in memory while being created with a transient lifetime. The service was updated to use a singleton lifetime so the authorization state survives the Google round trip. If this error appears again, restart the backend and retry the flow from the beginning.',
    references: [
      { title: 'Google OAuth Configuration Guide', path: 'docs/GOOGLE_OAUTH_CONFIGURATION_GUIDE.md' },
      { title: 'OAuth Implementation Summary', path: 'docs/OAUTH_IMPLEMENTATION_SUMMARY.md' }
    ]
  },
  {
    id: 'gmail-usage',
    category: 'Gmail Integration',
    title: 'What does Gmail integration allow the app to do?',
    summary: 'The application can read messages, create drafts, and send emails after approval.',
    keywords: ['gmail', 'read emails', 'send emails', 'drafts', 'permissions'],
    answer: 'Gmail integration allows the application to read Gmail messages, create drafts, and send emails through the connected account. The product follows an approval-first model, so external actions are intended to happen only after explicit approval. The app also stores the connected account information needed for the integration flow.',
    references: [
      { title: 'Gmail Integration', path: 'docs/gmail-integration.md' },
      { title: 'Approval Engine', path: 'docs/approval-engine.md' }
    ]
  },
  {
    id: 'approvals-overview',
    category: 'Approvals',
    title: 'How does the approval system work?',
    summary: 'Potentially impactful external actions are gated behind explicit approvals.',
    keywords: ['approval', 'approvals', 'execute', 'safety', 'external actions'],
    answer: 'ArvindJobHunter uses an approval-first execution model. Potentially impactful actions such as external communications or document-writing operations are presented for explicit approval before execution. This model provides traceability, control, and a clear separation between preparation and actual side effects.',
    references: [
      { title: 'Approval Engine', path: 'docs/approval-engine.md' },
      { title: 'Architecture', path: 'docs/architecture.md' }
    ]
  },
  {
    id: 'runtime-modes',
    category: 'Runtime & AI',
    title: 'What are the Demo, Dry Run, and Live modes?',
    summary: 'Execution mode controls whether the app stays offline, simulates actions, or performs real approved work.',
    keywords: ['demo', 'dry run', 'live', 'mode', 'runtime'],
    answer: 'Demo mode is intended for safe offline exploration. Dry Run exercises the workflow without real external effects. Live mode enables real work such as file updates and Gmail operations, but still requires user approval before execution. The selected mode affects how far the system is allowed to go during automation.',
    references: [
      { title: 'LLM Provider', path: 'docs/llm-provider.md' },
      { title: 'Approval Engine', path: 'docs/approval-engine.md' }
    ]
  },
  {
    id: 'json-storage',
    category: 'Data & Storage',
    title: 'Where is application data stored?',
    summary: 'The application currently uses JSON-based local persistence under the configured data directory.',
    keywords: ['storage', 'json', 'data directory', 'persistence', 'files'],
    answer: 'The application currently persists its data in JSON files under the configured storage data directory. This includes settings, audit information, approvals, jobs, applications, resumes, and integration-related state. The JSON storage documentation describes the expected layout and persistence behavior.',
    references: [
      { title: 'JSON Storage', path: 'docs/json-storage.md' },
      { title: 'Architecture', path: 'docs/architecture.md' }
    ]
  },
  {
    id: 'production-oauth',
    category: 'Deployment',
    title: 'What is required for production deployment of Google OAuth?',
    summary: 'Production requires environment-specific redirect URIs, secure secret storage, and verified operational configuration.',
    keywords: ['production', 'deploy', 'oauth', 'secrets', 'redirect uri', 'key vault'],
    answer: 'For production deployment, configure the Google OAuth Client ID, Client Secret, and Redirect URI per environment. Use secure secret storage such as environment variables or a secret manager, ensure production uses HTTPS redirect URIs, and verify the OAuth client configuration in Google Cloud Console. Production deployment guidance also includes validation and troubleshooting steps.',
    references: [
      { title: 'Production Deployment Guide', path: 'docs/PRODUCTION_DEPLOYMENT_GUIDE.md' },
      { title: 'Google OAuth User Flow', path: 'docs/GOOGLE_OAUTH_USER_FLOW.md' }
    ]
  }
];
