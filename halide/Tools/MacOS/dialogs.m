// macOS dialog sheets for EditSharp, driven by JSON snapshots through the esd_* exports; built by build.sh
#import <Cocoa/Cocoa.h>

typedef void (*EventCallback)(const char *json);

static EventCallback callback;
static NSMutableDictionary<NSNumber *, id> *dialogs;
static long nextDialog;

static const CGFloat Width = 420, Margin = 21, Gap = 12, Inner = 9, Line = 6, RowHeight = 48, Scroller = 18, MinButton = 81;
static const NSInteger MaxRows = 6;

static void emit(NSDictionary *event) {
    if (!callback) return;
    NSData *data = [NSJSONSerialization dataWithJSONObject:event options:0 error:nil];
    NSString *text = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
    callback(text.UTF8String);
}

static NSString *str(id value) { return [value isKindOfClass:NSString.class] ? value : @""; }
static NSArray *list(id value) { return [value isKindOfClass:NSArray.class] ? value : @[]; }

// roles, as the resource numbers them
enum { RoleNormal, RoleDefault, RoleCancel, RoleDestructive };

@interface ESPart : NSObject
@property NSDictionary *element;
@property NSView *row;
@property NSTextField *label;
@property NSControl *control;
@property NSControl *extra;
@property NSTextField *suffix;
@property NSMutableArray<NSArray *> *rows;       // [text, detail, button or NSNull]
@property NSMutableArray<NSButton *> *buttons;
@property double number;
@end

@implementation ESPart
@end

@interface ESPanel : NSPanel
@property (weak) id dialog;
@end

@interface ESDialog : NSObject <NSTextFieldDelegate, NSWindowDelegate>
@property long ident;
@property (weak) NSWindow *owner;
@property ESPanel *panel;
@property NSDictionary *shown;
@property NSString *key;
@property NSMutableArray<ESPart *> *parts;
@property NSMutableArray<NSButton *> *footer;   // in the snapshot's order
@property NSTextField *problem;
@property BOOL applying, closed;
- (void)close:(NSString *)button;
@end

@implementation ESPanel
- (BOOL)canBecomeKeyWindow { return YES; }
// escape with no Cancel button, and the close box when it's a window of its own
- (void)cancelOperation:(id)sender { [(ESDialog *)self.dialog close:nil]; }
@end

static BOOL labelled(NSDictionary *e) {
    NSString *kind = str(e[@"Kind"]);
    return str(e[@"Label"]).length > 0 && ([kind isEqual:@"field"] || [kind isEqual:@"path"] || [kind isEqual:@"dropdown"] || [kind isEqual:@"number"]);
}

// what decides the controls; anything else changes in place
static NSString *keyOf(NSDictionary *s) {
    NSMutableString *key = [NSMutableString string];
    for (NSDictionary *e in list(s[@"Elements"])) {
        [key appendFormat:@"%@;%d;%@;%d;%lu;", e[@"Kind"], labelled(e), [list(e[@"Options"]) componentsJoinedByString:@","], str(e[@"Suffix"]).length > 0, (unsigned long)list(e[@"ListButtons"]).count];
        for (NSDictionary *r in list(e[@"Rows"])) [key appendFormat:@"%d", str(r[@"ButtonText"]).length > 0];
        [key appendString:@"|"];
    }
    for (NSDictionary *b in list(s[@"Buttons"])) [key appendFormat:@"%@,", b[@"Role"]];
    return key;
}

static NSString *format(double value) {
    NSNumberFormatter *f = [NSNumberFormatter new];
    f.numberStyle = NSNumberFormatterDecimalStyle;
    f.maximumFractionDigits = 3;
    f.usesGroupingSeparator = NO;
    return [f stringFromNumber:@(value)];
}

static NSTextField *wrapping(NSString *text, NSFont *font, NSColor *color) {
    NSTextField *field = [NSTextField wrappingLabelWithString:text ?: @""];
    field.font = font;
    field.textColor = color;
    field.selectable = NO;
    field.preferredMaxLayoutWidth = Width - 2 * Margin;
    return field;
}

static NSStackView *stack(NSUserInterfaceLayoutOrientation orientation, CGFloat spacing, NSArray<NSView *> *views) {
    NSStackView *s = [NSStackView stackViewWithViews:views];
    s.orientation = orientation;
    s.spacing = spacing;
    s.alignment = orientation == NSUserInterfaceLayoutOrientationVertical ? NSLayoutAttributeLeading : NSLayoutAttributeCenterY;
    return s;
}

static void collect(NSView *view, NSMutableArray *into) {
    for (NSView *child in view.subviews) {
        [into addObject:child];
        collect(child, into);
    }
}

@implementation ESDialog

- (void)build:(NSDictionary *)snapshot {
    self.shown = snapshot;
    self.key = keyOf(snapshot);
    self.applying = YES;
    self.parts = [NSMutableArray array];
    self.footer = [NSMutableArray array];

    NSArray *elements = list(snapshot[@"Elements"]);
    CGFloat labels = 0;
    for (NSDictionary *e in elements) {
        if (!labelled(e)) continue;
        NSTextField *measure = [NSTextField labelWithString:[str(e[@"Label"]) stringByAppendingString:@":"]];
        labels = MAX(labels, ceil(measure.fittingSize.width));
    }

    NSMutableArray<NSView *> *rows = [NSMutableArray array];
    for (NSDictionary *e in elements) {
        ESPart *part = [self part:e labels:labels];
        [self.parts addObject:part];
        [rows addObject:part.row];
    }

    self.problem = wrapping(@"", [NSFont systemFontOfSize:NSFont.smallSystemFontSize], NSColor.systemRedColor);
    [rows addObject:self.problem];

    NSStackView *body = stack(NSUserInterfaceLayoutOrientationVertical, Gap, rows);
    body.edgeInsets = NSEdgeInsetsMake(Margin, Margin, Margin, Margin);
    for (NSView *row in rows) [row.widthAnchor constraintEqualToConstant:Width - 2 * Margin].active = YES;

    NSView *bottom = [NSView new];
    [bottom.heightAnchor constraintEqualToConstant:Margin].active = YES;
    NSStackView *content = stack(NSUserInterfaceLayoutOrientationVertical, 0, @[body, [self buttonBar:snapshot], bottom]);
    content.alignment = NSLayoutAttributeCenterX;
    [content.widthAnchor constraintEqualToConstant:Width].active = YES;
    [body.widthAnchor constraintEqualToConstant:Width].active = YES;

    self.panel.contentView = content;
    self.applying = NO;
    [self apply:snapshot];
}

- (ESPart *)part:(NSDictionary *)e labels:(CGFloat)labels {
    ESPart *part = [ESPart new];
    part.element = e;
    part.number = [e[@"Number"] doubleValue];
    NSString *kind = str(e[@"Kind"]);
    NSMutableArray<NSView *> *views = [NSMutableArray array];

    if (labelled(e)) {
        part.label = [NSTextField labelWithString:@""];
        part.label.alignment = NSTextAlignmentRight;
        [part.label.widthAnchor constraintEqualToConstant:labels].active = YES;
        [views addObject:part.label];
    }

    if ([kind isEqual:@"text"]) {
        part.control = wrapping(@"", [NSFont systemFontOfSize:NSFont.systemFontSize], NSColor.labelColor);
        part.row = part.control;
        return part;
    }

    if ([kind isEqual:@"field"] || [kind isEqual:@"path"]) {
        NSTextField *field = [NSTextField textFieldWithString:@""];
        field.delegate = self;
        part.control = field;
        [views addObject:field];
        [field setContentHuggingPriority:NSLayoutPriorityDefaultLow forOrientation:NSLayoutConstraintOrientationHorizontal];
        if ([kind isEqual:@"path"]) {
            part.extra = [NSButton buttonWithTitle:@"Choose…" target:self action:@selector(choose:)];
            [views addObject:part.extra];
        }
    } else if ([kind isEqual:@"dropdown"]) {
        NSPopUpButton *popup = [[NSPopUpButton alloc] initWithFrame:NSZeroRect pullsDown:NO];
        [popup addItemsWithTitles:list(e[@"Options"])];
        popup.target = self;
        popup.action = @selector(picked:);
        part.control = popup;
        [views addObject:popup];
    } else if ([kind isEqual:@"number"]) {
        NSTextField *field = [NSTextField textFieldWithString:@""];
        field.delegate = self;
        [field.widthAnchor constraintEqualToConstant:72].active = YES;
        NSStepper *stepper = [NSStepper new];
        stepper.minValue = [e[@"Min"] doubleValue];
        stepper.maxValue = [e[@"Max"] doubleValue];
        stepper.increment = [e[@"Step"] doubleValue] > 0 ? [e[@"Step"] doubleValue] : 1;
        stepper.valueWraps = NO;
        stepper.target = self;
        stepper.action = @selector(stepped:);
        part.control = field;
        part.extra = stepper;
        [views addObjectsFromArray:@[field, stepper]];
        if (str(e[@"Suffix"]).length > 0) {
            part.suffix = [NSTextField labelWithString:@""];
            [views addObject:part.suffix];
        }
    } else if ([kind isEqual:@"checkbox"]) {
        if (labels > 0) {
            NSView *indent = [NSView new];
            [indent.widthAnchor constraintEqualToConstant:labels].active = YES;
            [views addObject:indent];
        }
        part.control = [NSButton checkboxWithTitle:@"" target:self action:@selector(checked:)];
        [views addObject:part.control];
    } else if ([kind isEqual:@"list"]) {
        part.row = [self listPart:part];
        return part;
    }

    NSStackView *row = stack(NSUserInterfaceLayoutOrientationHorizontal, Inner, views);
    if ([kind isEqual:@"field"] || [kind isEqual:@"path"] || [kind isEqual:@"dropdown"]) row.distribution = NSStackViewDistributionFill;
    else {
        // a spacer takes what's left, so the controls keep together
        NSView *rest = [NSView new];
        [rest setContentHuggingPriority:1 forOrientation:NSLayoutConstraintOrientationHorizontal];
        [row addView:rest inGravity:NSStackViewGravityLeading];
    }
    part.row = row;
    return part;
}

- (NSView *)listPart:(ESPart *)part {
    NSDictionary *e = part.element;
    int index = [e[@"Index"] intValue];
    NSMutableArray<NSView *> *views = [NSMutableArray array];

    if (str(e[@"Label"]).length > 0) {
        part.label = [NSTextField labelWithString:@""];
        part.label.font = [NSFont boldSystemFontOfSize:NSFont.systemFontSize];
        [views addObject:part.label];
    }

    part.rows = [NSMutableArray array];
    NSMutableArray<NSView *> *lines = [NSMutableArray array];
    for (NSDictionary *r in list(e[@"Rows"])) {
        int row = [r[@"Index"] intValue];
        NSTextField *text = [NSTextField labelWithString:@""];
        text.lineBreakMode = NSLineBreakByTruncatingTail;
        NSTextField *detail = [NSTextField labelWithString:@""];
        detail.font = [NSFont systemFontOfSize:NSFont.smallSystemFontSize];
        detail.textColor = NSColor.secondaryLabelColor;
        detail.lineBreakMode = NSLineBreakByTruncatingMiddle;
        for (NSTextField *t in @[text, detail]) [t setContentCompressionResistancePriority:NSLayoutPriorityDefaultLow forOrientation:NSLayoutConstraintOrientationHorizontal];

        NSStackView *words = stack(NSUserInterfaceLayoutOrientationVertical, Line, @[text, detail]);
        NSStackView *lineView = stack(NSUserInterfaceLayoutOrientationHorizontal, Inner, @[]);
        [lineView addView:words inGravity:NSStackViewGravityLeading];
        id button = NSNull.null;
        if (str(r[@"ButtonText"]).length > 0) {
            NSButton *press = [NSButton buttonWithTitle:@"" target:self action:@selector(listPressed:)];
            press.identifier = [NSString stringWithFormat:@"%d:%d:-1", index, row];
            [lineView addView:press inGravity:NSStackViewGravityTrailing];
            button = press;
        }
        [lineView.heightAnchor constraintEqualToConstant:RowHeight - Inner].active = YES;
        [lines addObject:lineView];
        [part.rows addObject:@[text, detail, button]];
    }

    if (lines.count > 0) {
        NSStackView *rowsView = stack(NSUserInterfaceLayoutOrientationVertical, Inner, lines);
        for (NSView *line in lines) [line.widthAnchor constraintEqualToConstant:Width - 2 * Margin - (lines.count > MaxRows ? Scroller : 0)].active = YES;

        if (lines.count > MaxRows) {
            // a long list scrolls in a box of its own
            NSScrollView *scroll = [NSScrollView new];
            scroll.hasVerticalScroller = YES;
            scroll.drawsBackground = NO;
            scroll.borderType = NSNoBorder;
            NSClipView *clip = scroll.contentView;
            rowsView.translatesAutoresizingMaskIntoConstraints = NO;
            scroll.documentView = [self flipped:rowsView];
            [scroll.heightAnchor constraintEqualToConstant:MaxRows * RowHeight - Inner].active = YES;
            [scroll.widthAnchor constraintEqualToConstant:Width - 2 * Margin].active = YES;
            [NSLayoutConstraint activateConstraints:@[
                [scroll.documentView.topAnchor constraintEqualToAnchor:clip.topAnchor],
                [scroll.documentView.leadingAnchor constraintEqualToAnchor:clip.leadingAnchor],
                [scroll.documentView.widthAnchor constraintEqualToAnchor:clip.widthAnchor],
            ]];
            [views addObject:scroll];
        } else [views addObject:rowsView];
    }

    part.buttons = [NSMutableArray array];
    NSMutableArray<NSView *> *under = [NSMutableArray array];
    for (NSDictionary *b in list(e[@"ListButtons"])) {
        NSButton *press = [NSButton buttonWithTitle:@"" target:self action:@selector(listPressed:)];
        press.identifier = [NSString stringWithFormat:@"%d:-1:%d", index, [b[@"Index"] intValue]];
        [part.buttons addObject:press];
        [under addObject:press];
    }
    if (under.count > 0) [views addObject:stack(NSUserInterfaceLayoutOrientationHorizontal, Inner, under)];

    return stack(NSUserInterfaceLayoutOrientationVertical, Inner, views);
}

// a document view that lays out top down, so a scroll box starts at the first row
- (NSView *)flipped:(NSView *)inner {
    NSView *holder = [[NSClassFromString(@"ESFlippedView") alloc] init];
    holder.translatesAutoresizingMaskIntoConstraints = NO;
    [holder addSubview:inner];
    [NSLayoutConstraint activateConstraints:@[
        [inner.topAnchor constraintEqualToAnchor:holder.topAnchor],
        [inner.leadingAnchor constraintEqualToAnchor:holder.leadingAnchor],
        [inner.trailingAnchor constraintEqualToAnchor:holder.trailingAnchor],
        [inner.bottomAnchor constraintEqualToAnchor:holder.bottomAnchor],
    ]];
    return holder;
}

// macOS order: others at the leading edge in the resource's order, then Cancel, then the default rightmost
- (NSView *)buttonBar:(NSDictionary *)snapshot {
    NSMutableArray<NSView *> *others = [NSMutableArray array];
    NSButton *cancel = nil, *enter = nil;

    for (NSDictionary *b in list(snapshot[@"Buttons"])) {
        int role = [b[@"Role"] intValue];
        NSButton *button = [NSButton buttonWithTitle:@"" target:self action:@selector(footerPressed:)];
        button.identifier = str(b[@"Id"]);
        [button.widthAnchor constraintGreaterThanOrEqualToConstant:MinButton].active = YES;
        [self.footer addObject:button];

        if (role == RoleDefault && !enter) {
            button.keyEquivalent = @"\r";
            enter = button;
        } else if (role == RoleCancel && !cancel) {
            button.keyEquivalent = @"\033";
            cancel = button;
        } else {
            if (role == RoleDestructive) {
                if (@available(macOS 11.0, *)) button.hasDestructiveAction = YES;
            }
            [others addObject:button];
        }
    }

    NSStackView *bar = stack(NSUserInterfaceLayoutOrientationHorizontal, Gap, @[]);
    bar.edgeInsets = NSEdgeInsetsMake(0, Margin, 0, Margin);
    for (NSView *v in others) [bar addView:v inGravity:NSStackViewGravityLeading];
    if (cancel) [bar addView:cancel inGravity:NSStackViewGravityTrailing];
    if (enter) [bar addView:enter inGravity:NSStackViewGravityTrailing];
    [bar.widthAnchor constraintEqualToConstant:Width].active = YES;
    return bar;
}

// every value and state onto the controls, then sized again
- (void)apply:(NSDictionary *)snapshot {
    self.shown = snapshot;
    self.applying = YES;
    self.panel.title = str(snapshot[@"Title"]);

    NSArray *elements = list(snapshot[@"Elements"]);
    for (NSUInteger i = 0; i < self.parts.count && i < elements.count; i++) {
        ESPart *part = self.parts[i];
        NSDictionary *e = part.element = elements[i];
        NSString *kind = str(e[@"Kind"]);
        BOOL enabled = [e[@"Enabled"] boolValue];

        part.row.hidden = ![e[@"Visible"] boolValue];
        if (part.label) part.label.stringValue = [kind isEqual:@"list"] ? str(e[@"Label"]) : [str(e[@"Label"]) stringByAppendingString:@":"];
        if ([part.control respondsToSelector:@selector(setEnabled:)] && ![kind isEqual:@"text"]) part.control.enabled = enabled;
        part.extra.enabled = enabled;

        if ([kind isEqual:@"text"]) {
            NSTextField *text = (NSTextField *)part.control;
            int style = [e[@"TextStyle"] intValue];
            text.stringValue = str(e[@"Text"]);
            text.font = style == 1 ? [NSFont boldSystemFontOfSize:NSFont.systemFontSize] : [NSFont systemFontOfSize:NSFont.smallSystemFontSize];
            text.textColor = style == 2 ? NSColor.systemRedColor : NSColor.labelColor;
        } else if ([kind isEqual:@"field"] || [kind isEqual:@"path"]) {
            NSTextField *field = (NSTextField *)part.control;
            if (!field.currentEditor && ![field.stringValue isEqual:str(e[@"Value"])]) field.stringValue = str(e[@"Value"]);
            field.placeholderString = str(e[@"Placeholder"]);
        } else if ([kind isEqual:@"dropdown"]) {
            [(NSPopUpButton *)part.control selectItemAtIndex:[e[@"Selected"] integerValue]];
        } else if ([kind isEqual:@"number"]) {
            part.number = [e[@"Number"] doubleValue];
            NSTextField *field = (NSTextField *)part.control;
            if (!field.currentEditor) field.stringValue = format(part.number);
            ((NSStepper *)part.extra).doubleValue = part.number;
            if (part.suffix) part.suffix.stringValue = str(e[@"Suffix"]);
        } else if ([kind isEqual:@"checkbox"]) {
            NSButton *box = (NSButton *)part.control;
            box.title = str(e[@"Text"]);
            box.state = [e[@"Checked"] boolValue] ? NSControlStateValueOn : NSControlStateValueOff;
        } else if ([kind isEqual:@"list"]) {
            NSArray *rows = list(e[@"Rows"]);
            for (NSUInteger r = 0; r < part.rows.count && r < rows.count; r++) {
                NSDictionary *row = rows[r];
                ((NSTextField *)part.rows[r][0]).stringValue = str(row[@"Text"]);
                ((NSTextField *)part.rows[r][1]).stringValue = str(row[@"Detail"]);
                if ([part.rows[r][2] isKindOfClass:NSButton.class]) {
                    NSButton *press = part.rows[r][2];
                    press.title = str(row[@"ButtonText"]);
                    press.enabled = enabled && [row[@"ButtonEnabled"] boolValue];
                }
            }
            NSArray *buttons = list(e[@"ListButtons"]);
            for (NSUInteger b = 0; b < part.buttons.count && b < buttons.count; b++) part.buttons[b].title = str(buttons[b][@"Text"]);
        }
    }

    NSArray *buttons = list(snapshot[@"Buttons"]);
    for (NSUInteger b = 0; b < self.footer.count && b < buttons.count; b++) self.footer[b].title = str(buttons[b][@"Text"]);

    [self applyProblem];
    self.applying = NO;
    [self fit];
}

- (void)applyProblem {
    id problem = self.shown[@"Problem"];
    BOOL has = [problem isKindOfClass:NSString.class];
    self.problem.stringValue = has ? problem : @"";
    self.problem.hidden = !has;

    NSArray *buttons = list(self.shown[@"Buttons"]);
    for (NSUInteger b = 0; b < self.footer.count && b < buttons.count; b++)
        if ([buttons[b][@"Role"] intValue] == RoleDefault) self.footer[b].enabled = !has;
}

- (void)fit {
    NSView *content = self.panel.contentView;
    [content layoutSubtreeIfNeeded];
    NSSize size = NSMakeSize(Width, content.fittingSize.height);
    NSRect frame = [self.panel frameRectForContentRect:NSMakeRect(0, 0, size.width, size.height)];
    NSRect now = self.panel.frame;
    frame.origin = NSMakePoint(now.origin.x, NSMaxY(now) - frame.size.height);
    [self.panel setFrame:frame display:YES animate:self.panel.isVisible];
}

- (void)update:(NSDictionary *)snapshot {
    if ([keyOf(snapshot) isEqual:self.key]) {
        [self apply:snapshot];
        return;
    }
    [self build:snapshot];
    [self focusFirst];
}

- (void)focusFirst {
    for (ESPart *part in self.parts) {
        NSString *kind = str(part.element[@"Kind"]);
        if (!part.row.hidden && part.control.enabled && ([kind isEqual:@"field"] || [kind isEqual:@"path"] || [kind isEqual:@"number"])) {
            [self.panel makeFirstResponder:part.control];
            return;
        }
    }
}

- (ESPart *)partOf:(id)control {
    for (ESPart *part in self.parts) if (part.control == control || part.extra == control) return part;
    return nil;
}

- (void)edited:(ESPart *)part value:(id)value {
    if (self.applying) return;
    emit(@{@"event": @"edited", @"dialog": @(self.ident), @"index": part.element[@"Index"], @"value": value});
}

// ---- what the user does ----

- (void)controlTextDidChange:(NSNotification *)note {
    ESPart *part = [self partOf:note.object];
    if (!part) return;
    NSString *text = ((NSTextField *)part.control).stringValue;

    if ([str(part.element[@"Kind"]) isEqual:@"number"]) {
        NSNumberFormatter *f = [NSNumberFormatter new];
        f.numberStyle = NSNumberFormatterDecimalStyle;
        NSNumber *typed = [f numberFromString:text];
        if (!typed) return;
        part.number = MIN(MAX(typed.doubleValue, [part.element[@"Min"] doubleValue]), [part.element[@"Max"] doubleValue]);
        ((NSStepper *)part.extra).doubleValue = part.number;
        [self edited:part value:@(part.number)];
    } else [self edited:part value:text];
}

- (void)controlTextDidEndEditing:(NSNotification *)note {
    ESPart *part = [self partOf:note.object];
    if ([str(part.element[@"Kind"]) isEqual:@"number"]) ((NSTextField *)part.control).stringValue = format(part.number);
}

- (void)picked:(NSPopUpButton *)sender {
    ESPart *part = [self partOf:sender];
    if (part) [self edited:part value:@(sender.indexOfSelectedItem)];
}

- (void)stepped:(NSStepper *)sender {
    ESPart *part = [self partOf:sender];
    if (!part) return;
    part.number = sender.doubleValue;
    ((NSTextField *)part.control).stringValue = format(part.number);
    [self edited:part value:@(part.number)];
}

- (void)checked:(NSButton *)sender {
    ESPart *part = [self partOf:sender];
    if (part) [self edited:part value:@(sender.state == NSControlStateValueOn)];
}

- (void)choose:(NSButton *)sender {
    ESPart *part = [self partOf:sender];
    if (part) emit(@{@"event": @"browse", @"dialog": @(self.ident), @"index": part.element[@"Index"]});
}

- (void)listPressed:(NSButton *)sender {
    NSArray *at = [sender.identifier componentsSeparatedByString:@":"];
    emit(@{@"event": @"list", @"dialog": @(self.ident), @"index": @([at[0] intValue]), @"row": @([at[1] intValue]), @"button": @([at[2] intValue])});
}

- (void)footerPressed:(NSButton *)sender {
    if (sender.keyEquivalent.length > 0 && [sender.keyEquivalent isEqual:@"\r"] && [self.shown[@"Problem"] isKindOfClass:NSString.class]) return;
    [self close:sender.identifier];
}

- (BOOL)windowShouldClose:(NSWindow *)window {
    [self close:nil];
    return NO;
}

- (void)close:(NSString *)button {
    if (self.closed) return;
    self.closed = YES;

    if (self.owner && self.panel.sheetParent) [self.owner endSheet:self.panel];
    else [self.panel orderOut:nil];

    [dialogs removeObjectForKey:@(self.ident)];
    emit(@{@"event": @"closed", @"dialog": @(self.ident), @"button": button ?: NSNull.null});
}

@end

@interface ESFlippedView : NSView
@end

@implementation ESFlippedView
- (BOOL)isFlipped { return YES; }
@end

static NSDictionary *parse(const char *json) {
    if (!json) return nil;
    NSData *data = [NSData dataWithBytes:json length:strlen(json)];
    id value = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
    return [value isKindOfClass:NSDictionary.class] ? value : nil;
}

__attribute__((visibility("default"))) void esd_set_callback(EventCallback fn) { callback = fn; }

__attribute__((visibility("default"))) long esd_show(void *owner, int dark, const char *json) {
    NSDictionary *snapshot = parse(json);
    if (!snapshot) return 0;
    if (!dialogs) dialogs = [NSMutableDictionary dictionary];

    ESDialog *dialog = [ESDialog new];
    dialog.ident = ++nextDialog;
    dialog.owner = (__bridge NSWindow *)owner;

    NSWindowStyleMask style = NSWindowStyleMaskTitled | (dialog.owner ? 0 : NSWindowStyleMaskClosable);
    dialog.panel = [[ESPanel alloc] initWithContentRect:NSMakeRect(0, 0, Width, 100) styleMask:style backing:NSBackingStoreBuffered defer:NO];
    dialog.panel.dialog = dialog;
    dialog.panel.delegate = dialog;
    dialog.panel.releasedWhenClosed = NO;
    dialog.panel.appearance = [NSAppearance appearanceNamed:dark ? NSAppearanceNameDarkAqua : NSAppearanceNameAqua];

    [dialog build:snapshot];
    dialogs[@(dialog.ident)] = dialog;

    if (dialog.owner) [dialog.owner beginSheet:dialog.panel completionHandler:nil];
    else {
        [dialog.panel center];
        [dialog.panel makeKeyAndOrderFront:nil];
    }
    [dialog focusFirst];
    return dialog.ident;
}

__attribute__((visibility("default"))) void esd_update(long ident, const char *json) {
    NSDictionary *snapshot = parse(json);
    ESDialog *dialog = dialogs[@(ident)];
    if (snapshot && dialog && !dialog.closed) [dialog update:snapshot];
}

__attribute__((visibility("default"))) void esd_set_problem(long ident, const char *problem) {
    ESDialog *dialog = dialogs[@(ident)];
    if (!dialog || dialog.closed) return;

    NSMutableDictionary *shown = [dialog.shown mutableCopy];
    shown[@"Problem"] = problem ? [NSString stringWithUTF8String:problem] : NSNull.null;
    dialog.shown = shown;
    [dialog applyProblem];
    [dialog fit];
}

// ---- for the probes: a dialog found by its title, worked the way a user would ----

static ESDialog *titled(const char *title) {
    NSString *wanted = [NSString stringWithUTF8String:title];
    for (ESDialog *dialog in dialogs.allValues) if ([dialog.panel.title isEqual:wanted]) return dialog;
    return nil;
}

static NSArray *views(ESDialog *dialog) {
    NSMutableArray *all = [NSMutableArray array];
    collect(dialog.panel.contentView, all);
    return all;
}

static NSButton *buttonTitled(ESDialog *dialog, const char *text) {
    NSString *wanted = [NSString stringWithUTF8String:text];
    for (NSView *v in views(dialog))
        if ([v isKindOfClass:NSButton.class] && [((NSButton *)v).title isEqual:wanted] && !v.isHiddenOrHasHiddenAncestor) return (NSButton *)v;
    return nil;
}

__attribute__((visibility("default"))) int esd_test_showing(const char *title) {
    ESDialog *dialog = titled(title);
    return dialog && dialog.panel.isVisible;
}

__attribute__((visibility("default"))) int esd_test_press(const char *title, const char *text) {
    ESDialog *dialog = titled(title);
    if (!dialog) return 0;
    NSButton *button = buttonTitled(dialog, text);
    if (button.enabled) [button performClick:nil];
    return 1;
}

__attribute__((visibility("default"))) int esd_test_enabled(const char *title, const char *text) {
    ESDialog *dialog = titled(title);
    NSButton *button = dialog ? buttonTitled(dialog, text) : nil;
    return button ? button.enabled : -1;
}

__attribute__((visibility("default"))) int esd_test_type(const char *title, const char *label, const char *text) {
    ESDialog *dialog = titled(title);
    if (!dialog) return 0;
    NSString *wanted = [NSString stringWithUTF8String:label];
    for (ESPart *part in dialog.parts) {
        if (![str(part.element[@"Label"]) isEqual:wanted] || ![part.control isKindOfClass:NSTextField.class]) continue;
        ((NSTextField *)part.control).stringValue = [NSString stringWithUTF8String:text];
        [dialog controlTextDidChange:[NSNotification notificationWithName:NSControlTextDidChangeNotification object:part.control]];
    }
    return 1;
}

// 0 enter, 1 escape, as key equivalents reach the sheet
__attribute__((visibility("default"))) int esd_test_key(const char *title, int key) {
    ESDialog *dialog = titled(title);
    if (!dialog) return 0;
    NSString *characters = key == 0 ? @"\r" : @"\033";
    NSEvent *event = [NSEvent keyEventWithType:NSEventTypeKeyDown location:NSZeroPoint modifierFlags:0 timestamp:0 windowNumber:dialog.panel.windowNumber
                                       context:nil characters:characters charactersIgnoringModifiers:characters isARepeat:NO keyCode:key == 0 ? 36 : 53];
    if (![dialog.panel performKeyEquivalent:event] && key == 1) [dialog.panel cancelOperation:nil];
    return 1;
}

__attribute__((visibility("default"))) int esd_test_text_shown(const char *title, const char *text) {
    ESDialog *dialog = titled(title);
    if (!dialog) return -1;
    NSString *wanted = [NSString stringWithUTF8String:text];
    for (NSView *v in views(dialog))
        if ([v isKindOfClass:NSTextField.class] && [((NSTextField *)v).stringValue isEqual:wanted] && !v.isHiddenOrHasHiddenAncestor) return 1;
    return 0;
}

// the area to capture, in screen points from the top left: the owner with its sheet, or the panel
__attribute__((visibility("default"))) int esd_test_frame(const char *title, double *rect) {
    ESDialog *dialog = titled(title);
    if (!dialog) return 0;
    NSRect f = dialog.owner ? NSUnionRect(dialog.owner.frame, dialog.panel.frame) : dialog.panel.frame;
    CGFloat top = NSMaxY(NSScreen.screens.firstObject.frame);
    rect[0] = f.origin.x;
    rect[1] = top - NSMaxY(f);
    rect[2] = f.size.width;
    rect[3] = f.size.height;
    return 1;
}
