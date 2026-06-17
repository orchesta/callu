import { useForm as useReactHookForm, UseFormProps, FieldValues } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "../utils/toast";
import { t } from "../locales/i18n";

/**
 * Enhanced useForm hook with automatic Zod validation and error handling
 */
export function useForm<
  TFieldValues extends FieldValues = FieldValues,
  TContext = unknown,
>(schema: z.ZodType<TFieldValues>, options?: Omit<UseFormProps<TFieldValues, TContext>, "resolver">) {
  const form = useReactHookForm<TFieldValues, TContext>({
    ...options,
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    resolver: zodResolver(schema as any) as any,
  });

  /**
   * Enhanced submit handler with automatic error toast
   */
  const handleSubmit = (
    onValid: (data: TFieldValues) => void | Promise<void>,
    onInvalid?: (errors: typeof form.formState.errors) => void
  ) => {
    return form.handleSubmit(onValid, (errors) => {
      const errorMessages = Object.entries(errors)
        .map(([, error]) => error?.message)
        .filter(Boolean)
        .join(", ");

      if (errorMessages) {
        toast.error(t("toast.validationError"), errorMessages);
      }

      onInvalid?.(errors);
    });
  };

  /**
   * Submit with async error handling
   */
  const handleSubmitAsync = (
    onValid: (data: TFieldValues) => Promise<void>,
    options?: {
      successMessage?: string;
      errorMessage?: string;
      onSuccess?: () => void;
      onError?: (error: Error) => void;
    }
  ) => {
    return handleSubmit(async (data) => {
      try {
        await onValid(data);

        if (options?.successMessage) {
          toast.success(t("common.success"), options.successMessage);
        }

        options?.onSuccess?.();
      } catch (error) {
        const errorMsg = error instanceof Error ? error.message : (options?.errorMessage || t("incidents.errorOccurred"));
        toast.error(t("common.error"), errorMsg);

        options?.onError?.(error instanceof Error ? error : new Error(errorMsg));
      }
    });
  };

  return {
    ...form,
    handleSubmit,
    handleSubmitAsync,
  };
}
