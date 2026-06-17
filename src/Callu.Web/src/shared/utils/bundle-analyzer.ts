/**
 * Bundle Size Analyzer Utilities
 * Helpers for monitoring and optimizing bundle size
 */

/**
 * Log chunk sizes in development
 */
export function logChunkSizes() {
  if (import.meta.env.DEV) {
    console.group('📦 Bundle Loading Info');
    console.log('Mode:', import.meta.env.MODE);
    console.log('Check Network tab for actual chunk sizes');
    console.groupEnd();
  }
}

/**
 * Detect and log large dependencies
 */
export function detectLargeDependencies() {
  if (import.meta.env.DEV) {
    const performance = window.performance;
    const resources = performance.getEntriesByType('resource') as PerformanceResourceTiming[];

    const largeResources = resources
      .filter((r) => r.transferSize > 100_000)
      .sort((a, b) => b.transferSize - a.transferSize)
      .slice(0, 10);

    if (largeResources.length > 0) {
      console.group('⚠️ Large Dependencies Detected');
      largeResources.forEach((resource) => {
        console.log(
          `${(resource.transferSize / 1024).toFixed(2)} KB - ${resource.name}`
        );
      });
      console.groupEnd();
    }
  }
}

/**
 * Monitor bundle size in production
 */
export function monitorBundleSize() {
  if (!import.meta.env.DEV) return;
  if (typeof window !== 'undefined' && 'performance' in window) {
    window.addEventListener('load', () => {
      const resources = performance.getEntriesByType('resource') as PerformanceResourceTiming[];

      const jsResources = resources.filter((r) =>
        r.name.endsWith('.js') || r.name.includes('/assets/')
      );

      const totalSize = jsResources.reduce((sum, r) => sum + (r.transferSize || 0), 0);
      const totalSizeKB = (totalSize / 1024).toFixed(2);

      console.log(`📊 Total JS Bundle Size: ${totalSizeKB} KB`);

      if (totalSize > 500_000) {
        console.warn(`⚠️ Bundle size is large (${totalSizeKB} KB). Consider code splitting.`);
      }
    });
  }
}

/**
 * Check for duplicate dependencies
 */
export function checkDuplicateDependencies() {
  if (import.meta.env.DEV) {
    const resources = performance.getEntriesByType('resource') as PerformanceResourceTiming[];

    const fileNames = resources
      .map((r) => r.name.split('/').pop() || '')
      .filter((name) => name.endsWith('.js'));

    const duplicates = fileNames.filter(
      (name, index, self) => self.indexOf(name) !== index
    );

    if (duplicates.length > 0) {
      console.warn('⚠️ Possible duplicate dependencies:', [...new Set(duplicates)]);
    }
  }
}

/**
 * Get bundle size report
 */
function getBundleSizeReport() {
  const resources = performance.getEntriesByType('resource') as PerformanceResourceTiming[];

  const jsResources = resources.filter((r) => r.name.endsWith('.js'));
  const cssResources = resources.filter((r) => r.name.endsWith('.css'));
  const imageResources = resources.filter((r) =>
    r.name.match(/\.(jpg|jpeg|png|gif|webp|avif|svg)$/)
  );

  const calculateTotal = (resources: PerformanceResourceTiming[]) =>
    resources.reduce((sum, r) => sum + (r.transferSize || 0), 0);

  return {
    js: {
      count: jsResources.length,
      totalSize: calculateTotal(jsResources),
      totalSizeKB: (calculateTotal(jsResources) / 1024).toFixed(2),
      resources: jsResources.map((r) => ({
        name: r.name.split('/').pop() || '',
        size: r.transferSize,
        sizeKB: (r.transferSize / 1024).toFixed(2),
      })),
    },
    css: {
      count: cssResources.length,
      totalSize: calculateTotal(cssResources),
      totalSizeKB: (calculateTotal(cssResources) / 1024).toFixed(2),
    },
    images: {
      count: imageResources.length,
      totalSize: calculateTotal(imageResources),
      totalSizeKB: (calculateTotal(imageResources) / 1024).toFixed(2),
    },
    total: {
      size: calculateTotal([...jsResources, ...cssResources, ...imageResources]),
      sizeKB: (
        calculateTotal([...jsResources, ...cssResources, ...imageResources]) / 1024
      ).toFixed(2),
    },
  };
}

/**
 * Initialize bundle monitoring
 */
export function initBundleMonitoring() {
  if (typeof window === 'undefined') return;

  window.addEventListener('load', () => {
    setTimeout(() => {
      logChunkSizes();
      detectLargeDependencies();
      monitorBundleSize();
      checkDuplicateDependencies();

      if (import.meta.env.DEV) {
        console.log('📦 Bundle Size Report:', getBundleSizeReport());
      }
    }, 1000);
  });
}
