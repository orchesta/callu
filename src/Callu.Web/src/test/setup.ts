import '@testing-library/jest-dom';
import { expect, afterEach } from 'vitest';
import { cleanup } from '@testing-library/react';
import * as matchers from '@testing-library/jest-dom/matchers';

expect.extend(matchers);

afterEach(() => {
  cleanup();
});

process.env.VITE_API_URL = 'http://localhost:5000/api';
process.env.VITE_API_TIMEOUT = '30000';
