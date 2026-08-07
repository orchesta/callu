import { Search } from "lucide-react";
import { Input } from "./ui/input";
import { t } from "@/shared/locales/i18n";

interface SearchInputProps {
    placeholder?: string;
    value: string;
    onChange: (value: string) => void;
    className?: string;
}

export function SearchInput({
    placeholder = t("common.searchPlaceholder"),
    value,
    onChange,
    className = "",
}: SearchInputProps) {
    return (
        <div className={`relative ${className}`}>
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground pointer-events-none z-10" />
            <Input
                placeholder={placeholder}
                value={value}
                onChange={(e) => onChange(e.target.value)}
                className="pl-10 bg-input-background backdrop-blur-sm"
            />
        </div>
    );
}
