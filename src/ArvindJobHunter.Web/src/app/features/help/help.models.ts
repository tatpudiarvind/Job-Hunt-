export type HelpCategory = 'Getting Started' | 'Google OAuth' | 'Gmail Integration' | 'Runtime & AI' | 'Approvals' | 'Data & Storage' | 'Deployment';

export interface HelpReference {
  title: string;
  path: string;
}

export interface HelpArticle {
  id: string;
  category: HelpCategory;
  title: string;
  summary: string;
  keywords: string[];
  answer: string;
  references: HelpReference[];
}

export interface HelpCategoryDefinition {
  key: HelpCategory;
  title: string;
  description: string;
}

export interface HelpChatMessage {
  id: string;
  role: 'user' | 'assistant';
  text: string;
  references?: HelpReference[];
  matchedArticleIds?: string[];
}
