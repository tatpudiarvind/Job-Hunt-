import { TestBed } from '@angular/core/testing';
import { ClientLogService } from './client-log.service';
import { environment } from '../../environments/environment';

describe('ClientLogService', () => {
  let service: ClientLogService;
  let fetchMock: ReturnType<typeof vi.fn>;
  let now: number;

  beforeEach(() => {
    now = 1_000_000;
    vi.spyOn(Date, 'now').mockImplementation(() => now);
    fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);
    TestBed.configureTestingModule({});
    service = TestBed.inject(ClientLogService);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('posts the error to the API client-log endpoint', () => {
    service.report('error', 'TypeError: x is undefined', 'at foo (main.js:1:2)');

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe(`${environment.apiBaseUrl}/api/client-logs`);
    expect(init.method).toBe('POST');
    expect(init.keepalive).toBe(true);
    const body = JSON.parse(init.body);
    expect(body.level).toBe('error');
    expect(body.message).toBe('TypeError: x is undefined');
    expect(body.stack).toBe('at foo (main.js:1:2)');
  });

  it('ignores empty messages', () => {
    service.report('error', '   ');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('suppresses duplicates within the dedupe window but reports them again later', () => {
    service.report('error', 'same');
    service.report('error', 'same');
    expect(fetchMock).toHaveBeenCalledTimes(1);

    now += ClientLogService.duplicateWindowMs + 1;
    service.report('error', 'same');
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('caps the number of reports per minute', () => {
    for (let i = 0; i < 25; i++) service.report('error', `error ${i}`);
    expect(fetchMock).toHaveBeenCalledTimes(ClientLogService.maxReportsPerMinute);

    now += 60_001;
    service.report('error', 'after a minute');
    expect(fetchMock).toHaveBeenCalledTimes(ClientLogService.maxReportsPerMinute + 1);
  });

  it('never throws when the API cannot be reached', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    expect(() => service.report('warning', 'offline')).not.toThrow();
    await Promise.resolve();
  });
});
