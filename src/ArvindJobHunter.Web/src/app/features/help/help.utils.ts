import { HelpArticle } from './help.models';

export function scoreHelpArticle(article: HelpArticle, query: string): number {
  const value = query.trim().toLowerCase();
  if (!value) return 1;

  let score = 0;
  if (article.title.toLowerCase().includes(value)) score += 6;
  if (article.summary.toLowerCase().includes(value)) score += 4;
  if (article.answer.toLowerCase().includes(value)) score += 3;

  for (const keyword of article.keywords) {
    const normalized = keyword.toLowerCase();
    if (normalized.includes(value) || value.includes(normalized)) score += 5;
  }

  for (const token of value.split(/\s+/).filter(Boolean)) {
    if (article.title.toLowerCase().includes(token)) score += 2;
    if (article.summary.toLowerCase().includes(token)) score += 1;
    if (article.answer.toLowerCase().includes(token)) score += 1;
    if (article.keywords.some(k => k.toLowerCase().includes(token))) score += 2;
  }

  return score;
}

export function queryHelpArticles(articles: HelpArticle[], query: string): HelpArticle[] {
  const value = query.trim();
  if (!value) return articles;

  return articles
    .map(article => ({ article, score: scoreHelpArticle(article, value) }))
    .filter(x => x.score > 0)
    .sort((a, b) => b.score - a.score || a.article.title.localeCompare(b.article.title))
    .map(x => x.article);
}
