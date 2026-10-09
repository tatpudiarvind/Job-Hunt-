import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SettingsStateService } from './settings-state.service';
import { environment } from '../../environments/environment';

describe('SettingsStateService', () => {
  let service: SettingsStateService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(SettingsStateService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('refresh updates the shared settings signal with the latest execution mode', async () => {
    const first = service.load();
    http.expectOne(`${environment.apiBaseUrl}/api/settings`).flush({
      mode: 'DRY_RUN', llmProvider: 'Demo', openAiConfigured: false, masterResumePath: '', masterResumeExists: false, dataDirectory: 'data'
    });
    await first;

    expect(service.settings()?.mode).toBe('DRY_RUN');

    const refreshed = service.refresh();
    http.expectOne(`${environment.apiBaseUrl}/api/settings`).flush({
      mode: 'DEMO', llmProvider: 'OpenAI', openAiConfigured: true, masterResumePath: '', masterResumeExists: false, dataDirectory: 'data'
    });
    await refreshed;

    expect(service.settings()?.mode).toBe('DEMO');
    expect(service.settings()?.llmProvider).toBe('OpenAI');
  });
});
