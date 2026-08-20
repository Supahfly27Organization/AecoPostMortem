import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'
import '@testing-library/jest-dom/vitest'

// `test.globals` is deliberately off (see vitest.config.ts), so testing-library's usual
// auto-detected cleanup never registers — without this, one test's rendered tree is still in the
// document when the next test's render runs.
afterEach(() => {
  cleanup()
})
