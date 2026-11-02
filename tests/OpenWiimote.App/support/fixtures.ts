import { test as base, expect } from '@playwright/test';

/**
 * The `test` every browser spec uses.
 *
 * `pageErrors` follows the page for the whole test and fails it when the page threw at any
 * point, so a stubbed snapshot or a render bug cannot pass as a green test. It is an `auto`
 * fixture because a spec that forgets to ask for it is exactly the spec that would miss the
 * error.
 */
export const test = base.extend<{ pageErrors: string[] }>({
  pageErrors: [
    async ({ page }, use) => {
      const errors: string[] = [];
      page.on('pageerror', (error) => errors.push(error.message));
      await use(errors);
      expect(errors, 'the page threw an uncaught error').toEqual([]);
    },
    { auto: true }
  ]
});

export { expect };
