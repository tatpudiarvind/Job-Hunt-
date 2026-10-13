import { Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatButtonModule } from '@angular/material/button';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { HELP_ARTICLES, HELP_CATEGORIES } from './help-content';
import { HelpArticle, HelpCategory, HelpChatMessage } from './help.models';
import { queryHelpArticles } from './help.utils';

@Component({
  selector: 'app-help',
  standalone: true,
  imports: [FormsModule, MatIconModule, MatInputModule, MatFormFieldModule, MatButtonModule, PageHeaderComponent],
  template: `
    <div class="page">
      <app-page-header eyebrow="Support" title="Help Center" icon="help" subtitle="Browse formal product documentation or ask questions against the documented guidance." />

      <section class="card stack">
        <div class="row gap-sm wrap">
          <button mat-flat-button [color]="view() === 'docs' ? 'primary' : undefined" type="button" (click)="view.set('docs')">
            <mat-icon class="material-symbols-rounded">menu_book</mat-icon>Docs center
          </button>
          <button mat-flat-button [color]="view() === 'chat' ? 'primary' : undefined" type="button" (click)="view.set('chat')">
            <mat-icon class="material-symbols-rounded">chat</mat-icon>Docs chat
          </button>
        </div>
      </section>

      @if (view() === 'docs') {
        <section class="card stack mt">
          <div class="card-title"><mat-icon class="material-symbols-rounded">library_books</mat-icon>Documentation Center</div>
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Search documents</mat-label>
            <input matInput [(ngModel)]="query" placeholder="Search by topic, keyword, or problem" />
          </mat-form-field>
          <div class="row gap-sm wrap">
            <button mat-stroked-button type="button" [color]="selectedCategory() === 'All' ? 'primary' : undefined" (click)="selectedCategory.set('All')">All</button>
            @for (category of categories; track category.key) {
              <button mat-stroked-button type="button" [color]="selectedCategory() === category.key ? 'primary' : undefined" (click)="selectedCategory.set(category.key)">{{ category.title }}</button>
            }
          </div>

          @if (selectedArticle(); as article) {
            <section class="article-detail">
              <div class="row between wrap gap-sm">
                <div>
                  <div class="eyebrow">{{ article.category }}</div>
                  <h2>{{ article.title }}</h2>
                </div>
                <button mat-stroked-button type="button" (click)="selectedArticle.set(null)">
                  <mat-icon class="material-symbols-rounded">arrow_back</mat-icon>Back to results
                </button>
              </div>
              <p class="muted">{{ article.summary }}</p>
              <p>{{ article.answer }}</p>
              <div class="refs small">
                <b>References</b>
                <ul>
                  @for (reference of article.references; track reference.path) {
                    <li><span class="mono">{{ reference.path }}</span> — {{ reference.title }}</li>
                  }
                </ul>
              </div>
              @if (relatedArticles().length) {
                <div class="stack">
                  <b>Related topics</b>
                  <div class="row gap-sm wrap">
                    @for (related of relatedArticles(); track related.id) {
                      <button mat-stroked-button type="button" (click)="openArticle(related)">{{ related.title }}</button>
                    }
                  </div>
                </div>
              }
            </section>
          } @else {
            <div class="grid-2 docs-grid">
              @for (category of visibleCategories(); track category.key) {
                <section class="doc-group">
                  <div class="group-header">
                    <div class="eyebrow">{{ category.title }}</div>
                    <p class="muted small">{{ category.description }}</p>
                  </div>
                  @for (article of articlesByCategory(category.key); track article.id) {
                    <article class="doc-card" (click)="openArticle(article)">
                      <h3>{{ article.title }}</h3>
                      <p class="muted">{{ article.summary }}</p>
                      <div class="refs small">
                        <b>References</b>
                        <ul>
                          @for (reference of article.references; track reference.path) {
                            <li><span class="mono">{{ reference.path }}</span></li>
                          }
                        </ul>
                      </div>
                    </article>
                  } @empty {
                    <div class="empty small">
                      <mat-icon class="material-symbols-rounded">filter_alt_off</mat-icon>
                      No documents match the current search and category filter.
                    </div>
                  }
                </section>
              }
            </div>
          }
        </section>
      } @else {
        <section class="card stack mt">
          <div class="card-title"><mat-icon class="material-symbols-rounded">question_answer</mat-icon>Ask the docs</div>
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Your question</mat-label>
            <input matInput [(ngModel)]="chatInput" placeholder="Example: Why do I get redirect_uri_mismatch?" (keyup.enter)="askDocs()" />
          </mat-form-field>
          <div class="row gap-sm wrap">
            @for (suggestion of suggestions; track suggestion) {
              <button mat-stroked-button type="button" (click)="useSuggestion(suggestion)">{{ suggestion }}</button>
            }
            <button mat-flat-button color="primary" type="button" (click)="askDocs()" [disabled]="!chatInput().trim()">Ask</button>
          </div>
        </section>

        <section class="card stack mt">
          <div class="card-title"><mat-icon class="material-symbols-rounded">forum</mat-icon>Conversation</div>
          @for (message of conversation(); track message.id) {
            <article class="message" [class.user]="message.role === 'user'" [class.assistant]="message.role === 'assistant'">
              <div class="message-meta">{{ message.role === 'user' ? 'You' : 'Docs assistant' }}</div>
              <p class="message-text">{{ message.text }}</p>
              @if (message.references?.length) {
                <div class="refs small">
                  <b>References</b>
                  <ul>
                    @for (reference of message.references!; track reference.path) {
                      <li><span class="mono">{{ reference.path }}</span> — {{ reference.title }}</li>
                    }
                  </ul>
                </div>
              }
              @if (message.matchedArticleIds?.length) {
                <div class="row gap-sm wrap">
                  @for (article of matchedArticles(message.matchedArticleIds!); track article.id) {
                    <button mat-stroked-button type="button" (click)="showFromChat(article)">{{ article.title }}</button>
                  }
                </div>
              }
            </article>
          } @empty {
            <div class="empty">
              <mat-icon class="material-symbols-rounded">chat</mat-icon>
              Ask a question and the Help Center will answer using the structured project documentation.
            </div>
          }
        </section>
      }
    </div>
  `,
  styles: [`
    .stack { display: grid; gap: 1rem; }
    .wrap { flex-wrap: wrap; }
    .docs-grid { align-items: start; }
    .doc-group { display: grid; gap: .85rem; border: 1px solid var(--ajh-border); border-radius: 12px; padding: 1rem; background: var(--ajh-surface); }
    .group-header { display: grid; gap: .35rem; }
    .doc-card { border-top: 1px solid var(--ajh-border); padding-top: .85rem; display: grid; gap: .45rem; cursor: pointer; }
    .doc-card:first-of-type { border-top: 0; padding-top: 0; }
    .doc-card:hover { background: #f7f9fc; border-radius: 8px; padding-left: .35rem; padding-right: .35rem; margin-left: -.35rem; margin-right: -.35rem; }
    .article-detail { display: grid; gap: 1rem; border: 1px solid var(--ajh-border); border-radius: 12px; padding: 1rem; background: var(--ajh-surface); }
    .message { display: grid; gap: .45rem; padding: 1rem; border-radius: 12px; border: 1px solid var(--ajh-border); }
    .message.user { background: var(--ajh-primary-soft); }
    .message.assistant { background: var(--ajh-surface); }
    .message-meta { font-size: .8rem; font-weight: 600; color: var(--ajh-primary); }
    .message-text { white-space: pre-line; }
    h2, h3 { margin: 0; }
    h3 { font-size: 1.05rem; }
    .eyebrow { font-size: .72rem; font-weight: 600; letter-spacing: .08em; text-transform: uppercase; color: var(--ajh-primary); }
    .refs ul { margin: .35rem 0 0; padding-left: 1.1rem; color: var(--ajh-muted); }
    .empty { display: flex; gap: .75rem; align-items: flex-start; color: var(--ajh-muted); }
    .empty mat-icon { color: var(--ajh-primary); }
    .mono { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; }
    .small { font-size: .9rem; }
  `]
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
