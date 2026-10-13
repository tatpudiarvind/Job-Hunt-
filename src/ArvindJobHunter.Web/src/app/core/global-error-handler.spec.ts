import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { GlobalErrorHandler } from './global-error-handler';
import { ClientLogService } from './client-log.service';

describe('GlobalErrorHandler', () => {
  let handler: GlobalErrorHandler;
  let report: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    report = vi.fn();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    TestBed.configureTestingModule({
      providers: [GlobalErrorHandler, { provide: ClientLogService, useValue: { report } }]
    });
    handler = TestBed.inject(GlobalErrorHandler);
  });

  afterEach(() => vi.restoreAllMocks());

  it('reports runtime errors with their stack and still logs to the console', () => {
    const error = new TypeError('job.match is undefined');

    handler.handleError(error);

    expect(console.error).toHaveBeenCalledWith(error);
    expect(report).toHaveBeenCalledWith('error', 'TypeError: job.match is undefined', error.stack);
  });

  it('unwraps unhandled promise rejections', () => {
    handler.handleError({ rejection: new Error('async failure') });
    expect(report).toHaveBeenCalledWith('error', 'Error: async failure', expect.any(String));
  });

  it('does not report HTTP errors, which the API already logged', () => {
    handler.handleError(new HttpErrorResponse({ status: 500, url: 'http://localhost:5228/api/jobs' }));
    expect(report).not.toHaveBeenCalled();
  });

  it('reports non-Error values as text', () => {
    handler.handleError('plain string failure');
    expect(report).toHaveBeenCalledWith('error', 'plain string failure');
  });
});
