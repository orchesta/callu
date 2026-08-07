import { toast as sonnerToast, type ExternalToast } from "sonner";
import { isApiError, isValidationError, getErrorMessage, getFieldErrors } from '../api/api-errors';
import { t } from '../locales/i18n';

const toastConfig: ExternalToast = {
  duration: 4000,
  style: {
    background: "hsl(var(--background))",
    border: "1px solid hsl(var(--border))",
    color: "hsl(var(--foreground))",
  },
};

export const toast = {
  success: (message: string, description?: string) => {
    sonnerToast.success(message, {
      ...toastConfig,
      description,
      classNames: {
        toast: "border-success-200 bg-success-50 dark:bg-success-950",
        title: "text-success-700 dark:text-success-300",
        description: "text-success-600 dark:text-success-400",
      },
    });
  },

  error: (message: string, description?: string) => {
    sonnerToast.error(message, {
      ...toastConfig,
      description,
      duration: 6000,
      classNames: {
        toast: "border-error-200 bg-error-50 dark:bg-error-950",
        title: "text-error-700 dark:text-error-300",
        description: "text-error-600 dark:text-error-400",
      },
    });
  },

  warning: (message: string, description?: string) => {
    sonnerToast.warning(message, {
      ...toastConfig,
      description,
      classNames: {
        toast: "border-warning-200 bg-warning-50 dark:bg-warning-950",
        title: "text-warning-700 dark:text-warning-300",
        description: "text-warning-600 dark:text-warning-400",
      },
    });
  },

  info: (message: string, description?: string) => {
    sonnerToast.info(message, {
      ...toastConfig,
      description,
      classNames: {
        toast: "border-brand-200 bg-brand-50 dark:bg-brand-950",
        title: "text-brand-700 dark:text-brand-300",
        description: "text-brand-600 dark:text-brand-400",
      },
    });
  },

  loading: (message: string, description?: string) => {
    return sonnerToast.loading(message, {
      ...toastConfig,
      description,
    });
  },

  promise: <T,>(
    promise: Promise<T>,
    {
      loading,
      success,
      error,
    }: {
      loading: string;
      success: string | ((data: T) => string);
      error: string | ((error: Error) => string);
    }
  ) => {
    return sonnerToast.promise(promise, {
      loading,
      success,
      error,
      ...toastConfig,
    });
  },

  dismiss: (toastId?: string | number) => {
    sonnerToast.dismiss(toastId);
  },
};

export const handleApiError = (error: unknown, fallbackMessage = t("incidents.errorOccurred")) => {
  if (import.meta.env.DEV) {
    console.error("API Error:", error);
  }

  if (isValidationError(error)) {
    const fieldMessages = error.errorFields
      .map((field: string) => {
        const errors = getFieldErrors(error, field);
        return `${field}: ${errors.join(', ')}`;
      })
      .join('\n');
    toast.error(t("toast.validationError"), fieldMessages || error.message);
  } else if (isApiError(error)) {
    toast.error(t("common.error"), getErrorMessage(error));
  } else if (error instanceof Error) {
    toast.error(t("common.error"), error.message);
  } else if (typeof error === "string") {
    toast.error(t("common.error"), error);
  } else {
    toast.error(t("common.error"), fallbackMessage);
  }
};

export const handleFormError = (errors: Record<string, { message?: string } | undefined>) => {
  const errorMessages = Object.entries(errors)
    .map(([field, error]) => {
      if (error?.message) {
        return `${field}: ${error.message}`;
      }
      return null;
    })
    .filter(Boolean)
    .join(", ");

  if (errorMessages) {
    toast.error(t("toast.validationError"), errorMessages);
  }
};