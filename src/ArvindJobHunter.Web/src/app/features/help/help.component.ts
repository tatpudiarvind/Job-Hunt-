import { Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { HELP_ARTICLES, HELP_CATEGORIES } from './help-content';
import { HelpArticle, HelpCategory, HelpChatMessage } from './help.models';
import { queryHelpArticles } from './help.utils';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';

@Component({
  selector: 'app-help',
  standalone: true,
  imports: [ButtonModule, InputTextModule, FormsModule, PageHeaderComponent],
  templateUrl: './help.component.html',
  styleUrl: './help.component.scss'
})
export class HelpComponent {
  readonly view = signal<'docs' | 'chat'>('docs');
  readonly query = signal('');
  readonly chatInput = signal('');
  readonly selectedCategory = signal<HelpCategory | 'All'>('All');
  readonly selectedArticle = signal<HelpArticle | null>(null);
  readonly categories = HELP_CATEGORIES;
  readonly suggestions = [
    'How do I configure Google OAuth?',
    'How do I fix redirect_uri_mismatch?',
    'How do I start the application?',
    'How does approval-first execution work?',
    'Where is application data stored?'
  ];
  readonly conversation = signal<HelpChatMessage[]>([]);

  readonly docsResults = computed(() => queryHelpArticles(HELP_ARTICLES, this.query()));
  readonly relatedArticles = computed(() => {
    const current = this.selectedArticle();
    if (!current) return [];
    return HELP_ARTICLES
      .filter(article => article.id !== current.id && (article.category === current.category || article.references.some(r => current.references.some(c => c.path === r.path))))
      .slice(0, 4);
  });
  readonly visibleCategories = computed(() => {
    const selected = this.selectedCategory();
    const categories = selected === 'All' ? this.categories : this.categories.filter(c => c.key === selected);
    return categories.filter(category => this.articlesByCategory(category.key).length > 0 || !this.query().trim());
  });

  articlesByCategory(category: HelpCategory): HelpArticle[] {
    return this.docsResults().filter(article => article.category === category);
  }

  matchedArticles(ids: string[]): HelpArticle[] {
    return HELP_ARTICLES.filter(article => ids.includes(article.id));
  }

  openArticle(article: HelpArticle): void {
    this.selectedArticle.set(article);
  }

  showFromChat(article: HelpArticle): void {
    this.view.set('docs');
    this.selectedArticle.set(article);
    this.selectedCategory.set(article.category);
    this.query.set(article.title);
  }

  useSuggestion(suggestion: string): void {
    this.chatInput.set(suggestion);
    this.askDocs();
  }

  askDocs(): void {
    const question = this.chatInput().trim();
    if (!question) return;

    const userMessage: HelpChatMessage = {
      id: `user-${Date.now()}`,
      role: 'user',
      text: question
    };

    const matches = queryHelpArticles(HELP_ARTICLES, question).slice(0, 3);
    const assistantText = matches.length
      ? matches.map((article, index) => `${index + 1}. ${article.title}: ${article.answer}`).join('\n\n')
      : 'I could not find a strong match in the current documentation. Try asking about Google OAuth, startup, approvals, runtime modes, deployment, or storage.';

    const assistantMessage: HelpChatMessage = {
      id: `assistant-${Date.now()}`,
      role: 'assistant',
      text: assistantText,
      references: matches.flatMap(article => article.references).filter((reference, index, all) => all.findIndex(x => x.path === reference.path) === index),
      matchedArticleIds: matches.map(article => article.id)
    };

    this.conversation.update(messages => [...messages, userMessage, assistantMessage]);
    this.chatInput.set('');
  }
}
