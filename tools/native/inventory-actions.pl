#!/usr/bin/env perl
# Inventory legacy event wiring. An event is evidence of a workflow to review,
# not proof that the native port implements it. Output is deterministic TSV.
use strict;
use warnings;
use File::Find;
use FindBin;
use Cwd qw(abs_path);
my $root = abs_path("$FindBin::Bin/../..");
my @files;
sub cell { my $value = shift // ''; $value =~ s/\s+$//; return length($value) ? $value : '(none)'; }
find(sub { push @files, $File::Find::name if /\.Designer\.cs$/ }, "$root/Chummer/Forms", "$root/Chummer/Controls");
print join("\t", qw(source line control event handler label translation_key)), "\n";
for my $path (sort @files) {
    open my $input, '<:encoding(UTF-8)', $path or die "$path: $!";
    my @lines = <$input>;
    my %metadata;
    for my $line (@lines) {
        if ($line =~ /this\.(\w+)\.(Text|Tag)\s*=\s*"((?:\\.|[^"\\])*)"/) {
            my ($control, $property, $value) = ($1, $2, $3);
            $value =~ s/\\[rnt]/ /g;
            $value =~ s/\\"/"/g;
            $metadata{$control}{$property} = $value;
        }
    }
    (my $source = $path) =~ s/^\Q$root\E\///;
    for my $i (0 .. $#lines) {
        if ($lines[$i] =~ /this\.(?:(\w+)\.)?(\w+)\s*\+=\s*new\s+[\w.<>]+\(\s*this\.(\w+)\s*\)/) {
            my ($control, $event, $handler) = ($1 // '(form)', $2, $3);
            print join("\t", $source, $i + 1, $control, $event, $handler,
                cell($metadata{$control}{Text}), cell($metadata{$control}{Tag})), "\n";
        }
    }
}
