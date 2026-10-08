import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiService } from './api.service';
import { environment } from '../../environments/environment';

describe('ApiService', () => {
  let api: ApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('requestEmailApproval sends the required send flag (approval to send by default)', async () => {
    const pending = api.requestEmailApproval('draft-1');
    const req = http.expectOne(`${environment.apiBaseUrl}/api/emails/draft-1/request-approval?send=true`);
    expect(req.request.method).toBe('POST');
    req.flush({ id: 'approval-1', actionType: 'SEND_EMAIL' });
    expect((await pending).actionType).toBe('SEND_EMAIL');
  });

  it('requestEmailApproval can ask for a Gmail draft instead', async () => {
    const pending = api.requestEmailApproval('draft-1', false);
    http.expectOne(`${environment.apiBaseUrl}/api/emails/draft-1/request-approval?send=false`).flush({ id: 'approval-2', actionType: 'CREATE_EMAIL_DRAFT' });
    expect((await pending).actionType).toBe('CREATE_EMAIL_DRAFT');
  });
});
