import { Injectable, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { Settings } from './models';

@Injectable({ providedIn: 'root' })
export class SettingsStateService {
  private readonly api = inject(ApiService);
  readonly settings = signal<Settings | null>(null);
  private loading: Promise<Settings> | null = null;

  async load(force = false): Promise<Settings> {
    if (!force && this.settings()) return this.settings()!;
    if (!force && this.loading) return this.loading;

    this.loading = this.api.settings().then(settings => {
      this.settings.set(settings);
      return settings;
    }).finally(() => {
      this.loading = null;
    });

    return this.loading;
  }

  async refresh(): Promise<Settings> {
    return this.load(true);
  }

  update(settings: Settings): void {
    this.settings.set(settings);
  }
}
