// the macOS context menu host: a tiny AppKit process that shows real
// NSMenus on behalf of EditSharp. an NSMenu can only track inside a modal
// run loop on its process's main thread, so this process is the one that
// pauses while a menu is up, and the app carries on.
//
// build:  sh build.sh
// run:    editsharp-menu <parent pid>
//
// commands arrive as one JSON object per line on stdin:
//   {"cmd":"show","id":N,"x":..,"y":..,"dark":true,"items":[...]}   show at an appkit screen point
//   {"cmd":"update","id":N,"items":[...]}                           show again at the same point, after a pick
//   {"cmd":"dismiss"} {"cmd":"highlightNext"} {"cmd":"activate"} {"cmd":"quit"}
// an item: {"kind":"button|separator|label|submenu","tag":N,"text":"","key":"","mask":N,
//           "bold":false,"enabled":true,"checked":false,"icon":"<base64 png>","items":[...]}
// events go out as one JSON object per line on stdout, carrying the id of the showing:
//   {"event":"ready"}
//   {"event":"opened","id":N,"x":..,"y":..,"w":..,"h":..}   the menu's frame, points, top-left origin
//   {"event":"picked","id":N,"tag":N}
//   {"event":"closed","id":N,"click":"left|right|"}         click: the press that dismissed it, if one did
#import <Cocoa/Cocoa.h>

static pid_t parentPid;
static NSMenu *menu;
static NSPoint at;
static long picked;
static long showingId;
static BOOL tracking;
static NSDictionary *pending;

static void emit(NSDictionary *event) {
    NSData *data = [NSJSONSerialization dataWithJSONObject:event options:0 error:nil];
    fwrite(data.bytes, 1, data.length, stdout);
    fputc('\n', stdout);
    fflush(stdout);
}

@interface MenuTarget : NSObject <NSMenuDelegate>
- (void)itemPicked:(NSMenuItem *)sender;
- (void)handle:(NSDictionary *)command;
- (void)reportFrame;
@end

static MenuTarget *target;

static NSMenu *build(NSArray *items) {
    NSMenu *built = [[NSMenu alloc] initWithTitle:@""];
    built.autoenablesItems = NO;

    for (NSDictionary *item in items) {
        NSString *kind = item[@"kind"];

        if ([kind isEqualToString:@"separator"]) {
            [built addItem:[NSMenuItem separatorItem]];
            continue;
        }

        NSString *text = item[@"text"] ?: @"";
        NSString *key = item[@"key"] ?: @"";
        long tag = [item[@"tag"] longValue];
        SEL action = tag > 0 ? @selector(itemPicked:) : nil;

        NSMenuItem *mi = [[NSMenuItem alloc] initWithTitle:text action:action keyEquivalent:key];
        mi.tag = tag;
        mi.target = tag > 0 ? target : nil;
        mi.enabled = [item[@"enabled"] boolValue];
        if ([item[@"checked"] boolValue]) mi.state = NSControlStateValueOn;
        if ([item[@"mask"] longValue] != 0) mi.keyEquivalentModifierMask = (NSEventModifierFlags)[item[@"mask"] unsignedLongValue];

        if ([item[@"bold"] boolValue]) {
            NSFont *font = [NSFont boldSystemFontOfSize:[NSFont systemFontSize]];
            mi.attributedTitle = [[NSAttributedString alloc] initWithString:text attributes:@{NSFontAttributeName: font}];
        }

        NSString *icon = item[@"icon"];
        if (icon.length > 0) {
            NSData *png = [[NSData alloc] initWithBase64EncodedString:icon options:0];
            NSImage *image = png ? [[NSImage alloc] initWithData:png] : nil;
            if (image) {
                image.size = NSMakeSize(16, 16);
                mi.image = image;
            }
        }

        if ([kind isEqualToString:@"submenu"]) mi.submenu = build(item[@"items"] ?: @[]);

        [built addItem:mi];
    }

    return built;
}

// which button's press ended tracking, if a press did: the menu swallows
// it, so the app is told and plays it itself
static NSString *dismissingClick(void) {
    NSEvent *event = [NSApp currentEvent];
    switch (event.type) {
        case NSEventTypeLeftMouseDown:
        case NSEventTypeLeftMouseUp:
            return @"left";
        case NSEventTypeRightMouseDown:
        case NSEventTypeRightMouseUp:
            return @"right";
        default:
            return @"";
    }
}

// macOS 14 on: activation is cooperative. the app yields it to this process
// before sending a show, so asking takes it; this process yields it back
// once the menu is gone. before 14 the old calls do both on their own
static void activate(void) {
    if (@available(macOS 14.0, *)) [NSApp activate];
    else {
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
        [NSApp activateIgnoringOtherApps:YES];
#pragma clang diagnostic pop
    }
}

static void giveBackActivation(void) {
    NSRunningApplication *parent = [NSRunningApplication runningApplicationWithProcessIdentifier:parentPid];
    if (!parent) return;

    if (@available(macOS 14.0, *)) {
        [NSApp yieldActivationToApplication:parent];
        [parent activateWithOptions:0];
    } else {
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
        [parent activateWithOptions:NSApplicationActivateIgnoringOtherApps];
#pragma clang diagnostic pop
    }
}

// tracks the menu on this thread, which is what pauses this process and not the app
static void track(void) {
    picked = 0;
    tracking = YES;
    menu.delegate = target;

    activate();
    fprintf(stderr, "menuhost: showing id=%ld at %.0f,%.0f active=%d\n", showingId, at.x, at.y, NSApp.isActive);
    [menu popUpMenuPositioningItem:nil atLocation:at inView:nil];

    tracking = NO;

    // why it ended, on stderr, which the app's own log carries
    NSEvent *last = [NSApp currentEvent];
    fprintf(stderr, "menuhost: tracking ended picked=%ld active=%d lastEvent=%ld\n", picked, NSApp.isActive, (long)last.type);

    if (picked > 0) emit(@{@"event": @"picked", @"id": @(showingId), @"tag": @(picked)});
    else emit(@{@"event": @"closed", @"id": @(showingId), @"click": pending ? @"" : dismissingClick()});

    // a show that arrived while this one was up goes next; otherwise the app gets the foreground back
    if (pending) {
        NSDictionary *next = pending;
        pending = nil;
        dispatch_async(dispatch_get_main_queue(), ^{ [target handle:next]; });
        return;
    }

    giveBackActivation();
}

static void postKey(unsigned short keyCode, NSString *characters) {
    for (NSNumber *type in @[@(NSEventTypeKeyDown), @(NSEventTypeKeyUp)]) {
        NSEvent *e = [NSEvent keyEventWithType:(NSEventType)type.unsignedIntegerValue location:NSZeroPoint modifierFlags:0 timestamp:0
                                  windowNumber:0 context:nil characters:characters charactersIgnoringModifiers:characters isARepeat:NO keyCode:keyCode];
        if (e) [NSApp postEvent:e atStart:NO];
    }
}

@implementation MenuTarget
- (void)itemPicked:(NSMenuItem *)sender { picked = sender.tag; }

// the menu's window exists once tracking starts; its frame is read in the tracking mode
- (void)menuWillOpen:(NSMenu *)opened {
    if (opened != menu) return;
    [self performSelector:@selector(reportFrame) withObject:nil afterDelay:0.05 inModes:@[NSEventTrackingRunLoopMode]];
}

- (void)reportFrame {
    CGFloat top = NSMaxY(NSScreen.screens.firstObject.frame);

    for (NSWindow *window in NSApp.windows) {
        if (!window.isVisible || ![NSStringFromClass(window.class) containsString:@"Menu"]) continue;

        NSRect f = window.frame;
        emit(@{@"event": @"opened", @"id": @(showingId), @"x": @(f.origin.x), @"y": @(top - NSMaxY(f)), @"w": @(f.size.width), @"h": @(f.size.height)});
        return;
    }
}

- (void)handle:(NSDictionary *)command {
    NSString *cmd = command[@"cmd"];

    if ([cmd isEqualToString:@"show"] || [cmd isEqualToString:@"update"]) {
        // a menu is up: it goes, and this one follows once it has
        if (tracking) {
            pending = command;
            [menu cancelTracking];
            return;
        }

        showingId = [command[@"id"] longValue];
        if ([cmd isEqualToString:@"show"]) {
            BOOL dark = [command[@"dark"] boolValue];
            NSApp.appearance = [NSAppearance appearanceNamed:dark ? NSAppearanceNameDarkAqua : NSAppearanceNameAqua];
            at = NSMakePoint([command[@"x"] doubleValue], [command[@"y"] doubleValue]);
        }

        menu = build(command[@"items"] ?: @[]);
        track();
    } else if ([cmd isEqualToString:@"dismiss"]) {
        [menu cancelTracking];
    } else if ([cmd isEqualToString:@"highlightNext"]) {
        postKey(125, [NSString stringWithFormat:@"%C", (unichar)NSDownArrowFunctionKey]);
    } else if ([cmd isEqualToString:@"activate"]) {
        postKey(36, @"\r");
    } else if ([cmd isEqualToString:@"quit"]) {
        [NSApp terminate:nil];
    }
}
@end

// stdin is read on its own thread; every command is handed to the main
// thread in the common modes, so it also reaches a menu that is tracking
static void readCommands(void) {
    char *line = NULL;
    size_t capacity = 0;
    ssize_t length;

    while ((length = getline(&line, &capacity, stdin)) > 0) {
        NSData *data = [NSData dataWithBytes:line length:(NSUInteger)length];
        NSDictionary *command = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
        if (![command isKindOfClass:[NSDictionary class]]) continue;

        [target performSelectorOnMainThread:@selector(handle:) withObject:command waitUntilDone:NO modes:@[NSRunLoopCommonModes]];
    }

    free(line);

    // the app went away with its end of the pipe
    dispatch_async(dispatch_get_main_queue(), ^{ [NSApp terminate:nil]; });
}

int main(int argc, const char *argv[]) {
    @autoreleasepool {
        parentPid = argc > 1 ? (pid_t)atoi(argv[1]) : 0;
        [NSApplication sharedApplication];
        [NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory];
        target = [MenuTarget new];

        [NSThread detachNewThreadWithBlock:^{ readCommands(); }];
        emit(@{@"event": @"ready"});
        [NSApp run];
    }
    return 0;
}
