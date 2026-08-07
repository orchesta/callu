// Header language switcher. The i18n module owns loading and re-rendering; the choice is also sent
// to the server, because it decides which language this person is spoken to in when they are paged.

import { Check, Languages } from "lucide-react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "../ui/dropdown-menu";
import { cultureFor, getLocale, setLocale, SUPPORTED_LOCALES, t } from "@/shared/locales/i18n";
import { useUpdateLanguage } from "@/features/profile/hooks/use-profile";
import { useAuth } from "@/shared/auth/auth.context";

export function LocaleSwitcher() {
  const current = getLocale();
  const { isAuthenticated } = useAuth();
  const updateLanguage = useUpdateLanguage();

  const choose = (code: (typeof SUPPORTED_LOCALES)[number]["code"]) => {
    void setLocale(code);

    // The screen switches either way; recording the choice is what makes a voice call speak it, and
    // a signed-out user has nowhere to record it.
    if (isAuthenticated) {
      updateLanguage.mutate(cultureFor(code));
    }
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button
          type="button"
          aria-label={t("a11y.changeLanguage")}
          className="inline-flex items-center justify-center gap-1 h-9 px-2 rounded-full text-muted-foreground hover:text-brand-500 hover:bg-muted transition-colors"
        >
          <Languages className="w-5 h-5" />
          <span className="text-xs font-semibold uppercase">{current}</span>
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-40 bg-card/95 backdrop-blur-xl border-border">
        {SUPPORTED_LOCALES.map((locale) => (
          <DropdownMenuItem
            key={locale.code}
            className="cursor-pointer justify-between"
            onClick={() => choose(locale.code)}
          >
            <span>{locale.nativeName}</span>
            {locale.code === current && <Check className="w-4 h-4 text-brand-500" />}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
