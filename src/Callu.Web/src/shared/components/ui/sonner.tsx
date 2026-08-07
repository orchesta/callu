import { Toaster as Sonner, ToasterProps } from "sonner";
import { t } from "@/shared/locales/i18n";

const Toaster = ({ toastOptions, ...props }: ToasterProps) => {
  return (
    <Sonner
      theme="dark"
      className="toaster group"
      style={
        {
          "--normal-bg": "var(--popover)",
          "--normal-text": "var(--popover-foreground)",
          "--normal-border": "var(--border)",
        } as React.CSSProperties
      }
      containerAriaLabel={t("a11y.toastNotifications")}
      toastOptions={{
        closeButtonAriaLabel: t("a11y.closeToast"),
        ...toastOptions,
      }}
      {...props}
    />
  );
};

export { Toaster };
