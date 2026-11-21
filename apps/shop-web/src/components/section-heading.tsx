import Link from 'next/link';

export function SectionHeading({
    eyebrow,
    title,
    subtitle,
    href,
    linkLabel = 'View all',
}: {
    eyebrow?: string;
    title: string;
    subtitle?: string | undefined;
    href?: string;
    linkLabel?: string;
}) {
    return (
        <div className="section-heading">
            <div>
                {eyebrow && <p className="eyebrow">{eyebrow}</p>}
                <h2>{title}</h2>
                {subtitle && <p className="section-heading__subtitle">{subtitle}</p>}
            </div>
            {href && (
                <Link className="text-link" href={href} prefetch={false}>
                    {linkLabel}
                    <span aria-hidden="true"> →</span>
                </Link>
            )}
        </div>
    );
}
