import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
  parseCommandLine: vi.fn(),
  run: vi.fn(),
}));

vi.mock('../src/cli/program.js', () => ({
  parseCommandLine: mocks.parseCommandLine,
}));

vi.mock('../src/application/html-css-indexer-application.js', () => ({
  HtmlCssIndexerApplication: class {
    run(options: unknown) {
      return mocks.run(options);
    }
  },
}));

describe('HTML/CSS indexer entrypoint', () => {
  const originalExitCode = process.exitCode;

  beforeEach(() => {
    vi.resetModules();
    vi.clearAllMocks();
  });

  afterEach(() => {
    process.exitCode = originalExitCode;
    vi.restoreAllMocks();
  });

  it('sets a successful exit code without forcing the process to exit', async () => {
    const options = { projectName: 'CodeMeridian' };
    mocks.parseCommandLine.mockResolvedValue(options);
    mocks.run.mockResolvedValue(0);
    const exitSpy = vi.spyOn(process, 'exit').mockImplementation((code) => {
      throw new Error(`process.exit(${code}) was called`);
    });

    await import('../src/index.js');

    expect(mocks.parseCommandLine).toHaveBeenCalledWith(process.argv);
    expect(mocks.run).toHaveBeenCalledWith(options);
    expect(process.exitCode).toBe(0);
    expect(exitSpy).not.toHaveBeenCalled();
  });

  it('sets a failure exit code without forcing the process to exit', async () => {
    mocks.parseCommandLine.mockRejectedValue(new Error('invalid options'));
    const errorSpy = vi
      .spyOn(console, 'error')
      .mockImplementation(() => undefined);
    const exitSpy = vi.spyOn(process, 'exit').mockImplementation((code) => {
      throw new Error(`process.exit(${code}) was called`);
    });

    await import('../src/index.js');

    expect(errorSpy).toHaveBeenCalledWith('error: invalid options');
    expect(mocks.run).not.toHaveBeenCalled();
    expect(process.exitCode).toBe(1);
    expect(exitSpy).not.toHaveBeenCalled();
  });
});
