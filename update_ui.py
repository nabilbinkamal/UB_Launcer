import sys

with open(r'c:\A - WEBSITE\UB LAUNCHER\Launcher.UI\Views\CreateServerView.axaml', 'r', encoding='utf-8') as f:
    lines = f.readlines()

# Find the start and end indices
start_idx = -1
end_idx = -1
for i, line in enumerate(lines):
    if '<Border Grid.Column="1" Classes="WorkspaceSection ServerCreatorPanel" VerticalAlignment="Top" IsVisible="{Binding IsManagerVisible}">' in line:
        start_idx = i
    if start_idx != -1 and i > start_idx and '<Border Grid.Column="1" Classes="WorkspaceSection ServerCreatorPanel" VerticalAlignment="Top" IsVisible="{Binding IsCreatePanelVisible}">' in line:
        end_idx = i
        break

if start_idx == -1 or end_idx == -1:
    print('Could not find the block')
    sys.exit(1)

new_content = \"\"\"                <Border Grid.Column=\"1\" Classes=\"WorkspaceSection ServerCreatorPanel\" VerticalAlignment=\"Stretch\" IsVisible=\"{Binding IsManagerVisible}\">
                    <Grid RowDefinitions=\"Auto,*\">
                        <Grid ColumnDefinitions=\"*,Auto\" ColumnSpacing=\"12\" Margin=\"0,0,0,16\">
                            <StackPanel Spacing=\"4\">
                                <TextBlock Text=\"{Binding SelectedServer.Name}\" Classes=\"WorkspaceSectionTitle\" TextTrimming=\"CharacterEllipsis\"/>
                                <TextBlock Text=\"{Binding SelectedServer.Endpoint}\" Classes=\"WorkspaceSecondary\" Foreground=\"#7FA9C4\"/>
                            </StackPanel>
                            <Border Grid.Column=\"1\" Classes=\"ServerStatusPill\" Classes.Running=\"{Binding SelectedServer.IsRunning}\" Classes.SetupRequired=\"{Binding SelectedServer.IsSetupRequired}\" VerticalAlignment=\"Top\">
                                <TextBlock Text=\"{Binding SelectedServer.Status}\" Classes=\"WorkspaceBadgeText\"/>
                            </Border>
                        </Grid>

                        <TabControl Grid.Row=\"1\" SelectedIndex=\"{Binding CurrentTab}\">
                            <TabControl.Styles>
                                <Style Selector=\"TabItem\">
                                    <Setter Property=\"FontSize\" Value=\"14\"/>
                                    <Setter Property=\"FontWeight\" Value=\"SemiBold\"/>
                                    <Setter Property=\"Margin\" Value=\"0,0,16,0\"/>
                                    <Setter Property=\"Padding\" Value=\"0,0,0,8\"/>
                                    <Setter Property=\"Foreground\" Value=\"#90A4AE\"/>
                                    <Setter Property=\"Background\" Value=\"Transparent\"/>
                                </Style>
                                <Style Selector=\"TabItem:selected\">
                                    <Setter Property=\"Foreground\" Value=\"#68DDA8\"/>
                                </Style>
                                <Style Selector=\"TabControl\">
                                    <Setter Property=\"Margin\" Value=\"0\"/>
                                    <Setter Property=\"Padding\" Value=\"0,16,0,0\"/>
                                </Style>
                            </TabControl.Styles>

                            <!-- Console Tab -->
                            <TabItem Header=\"Console\">
                                <Grid RowDefinitions=\"Auto,*,Auto\" Margin=\"0,8,0,0\">
                                    <Border Grid.Row=\"0\" Background=\"#0C151C\" BorderBrush=\"#1C2F40\" BorderThickness=\"1\" CornerRadius=\"8\" Padding=\"12\" Margin=\"0,0,0,12\">
                                        <Grid ColumnDefinitions=\"*,*,*,*\" ColumnSpacing=\"8\">
                                            <Button Content=\"Start\" Classes=\"ServerActionButton\" Background=\"#1E8E3E\" Foreground=\"White\" Command=\"{Binding ToggleSelectedServerCommand}\" IsEnabled=\"{Binding !SelectedServer.IsRunning}\" HorizontalAlignment=\"Stretch\" HorizontalContentAlignment=\"Center\"/>
                                            <Button Grid.Column=\"1\" Content=\"Restart\" Classes=\"ServerActionButton\" Background=\"#1A73E8\" Foreground=\"White\" Command=\"{Binding RestartSelectedServerCommand}\" IsEnabled=\"{Binding SelectedServer.IsRunning}\" HorizontalAlignment=\"Stretch\" HorizontalContentAlignment=\"Center\"/>
                                            <Button Grid.Column=\"2\" Content=\"Stop\" Classes=\"ServerActionButton\" Background=\"#F9AB00\" Foreground=\"#111\" Command=\"{Binding ToggleSelectedServerCommand}\" IsEnabled=\"{Binding SelectedServer.IsRunning}\" HorizontalAlignment=\"Stretch\" HorizontalContentAlignment=\"Center\"/>
                                            <Button Grid.Column=\"3\" Content=\"Kill\" Classes=\"ServerActionButton\" Background=\"#D93025\" Foreground=\"White\" Command=\"{Binding KillSelectedServerCommand}\" IsEnabled=\"{Binding SelectedServer.IsRunning}\" HorizontalAlignment=\"Stretch\" HorizontalContentAlignment=\"Center\"/>
                                        </Grid>
                                    </Border>
                                    
                                    <Border Grid.Row=\"1\" Background=\"#05090C\" BorderBrush=\"#152636\" BorderThickness=\"1\" CornerRadius=\"6\" Padding=\"8\">
                                        <ScrollViewer VerticalScrollBarVisibility=\"Auto\" HorizontalScrollBarVisibility=\"Disabled\">
                                            <TextBlock Text=\"{Binding SelectedServer.LogText}\" FontFamily=\"Consolas, Courier New, monospace\" FontSize=\"13\" Foreground=\"#E2E8ED\" TextWrapping=\"Wrap\"/>
                                        </ScrollViewer>
                                    </Border>
                                    
                                    <Grid Grid.Row=\"2\" ColumnDefinitions=\"*,Auto\" ColumnSpacing=\"8\" Margin=\"0,12,0,0\">
                                        <TextBox Text=\"{Binding ConsoleCommand}\" PlaceholderText=\"Type a command...\" Background=\"#0C151C\" BorderBrush=\"#1C2F40\" Classes=\"WorkspaceField ServerConsoleInput\" IsEnabled=\"{Binding SelectedServer.IsRunning}\" KeyDown=\"ConsoleCommand_OnKeyDown\"/>
                                        <Button Grid.Column=\"1\" Content=\"Send\" Classes=\"WorkspacePrimary\" Command=\"{Binding SendConsoleCommandCommand}\" IsEnabled=\"{Binding SelectedServer.IsRunning}\"/>
                                    </Grid>
                                </Grid>
                            </TabItem>

                            <!-- Network Tab -->
                            <TabItem Header=\"Network\">
                                <ScrollViewer VerticalScrollBarVisibility=\"Auto\">
                                    <StackPanel Spacing=\"16\" Margin=\"0,8,0,0\">
                                        <Border Classes=\"ServerNetworkCard\">
                                            <StackPanel Spacing=\"12\">
                                                <Grid ColumnDefinitions=\"*,Auto\" ColumnSpacing=\"12\">
                                                    <StackPanel Spacing=\"4\">
                                                        <TextBlock Text=\"Same Wi-Fi / LAN\" Classes=\"WorkspaceStatLabel\" Foreground=\"#68DDA8\"/>
                                                        <TextBlock Text=\"{Binding LanJoinAddress}\" Classes=\"ServerJoinAddress\"/>
                                                    </StackPanel>
                                                    <Button Grid.Column=\"1\" Content=\"Copy\" Classes=\"ServerActionButton\" Click=\"CopyLanAddress_OnClick\" VerticalAlignment=\"Center\"/>
                                                </Grid>
                                                <Border Classes=\"WorkspaceDivider\"/>
                                                <Grid ColumnDefinitions=\"*,Auto\" ColumnSpacing=\"12\">
                                                    <StackPanel Spacing=\"4\">
                                                        <TextBlock Text=\"Play from anywhere — UB Relay\" Classes=\"WorkspaceStatLabel\" Foreground=\"#82C7FF\"/>
                                                        <TextBlock Text=\"{Binding TunnelJoinAddress}\" Classes=\"ServerJoinAddress\" Foreground=\"#9BD9FF\"/>
                                                        <TextBlock Text=\"{Binding TunnelStatus}\" Classes=\"WorkspaceSecondary\" FontSize=\"11\" TextWrapping=\"Wrap\"/>
                                                    </StackPanel>
                                                    <StackPanel Grid.Column=\"1\" Spacing=\"7\" VerticalAlignment=\"Center\">
                                                        <Button Content=\"{Binding TunnelActionLabel}\" Classes=\"ServerActionButton RelayToggleButton\" Classes.Stop=\"{Binding IsTunnelRunning}\" Command=\"{Binding ToggleTunnelCommand}\"/>
                                                        <Button Content=\"Copy\" Classes=\"ServerActionButton\" Click=\"CopyTunnelAddress_OnClick\"/>
                                                    </StackPanel>
                                                </Grid>
                                            </StackPanel>
                                        </Border>
                                    </StackPanel>
                                </ScrollViewer>
                            </TabItem>

                            <!-- Plugins & Mods Tab -->
                            <TabItem Header=\"{Binding ExtensionHeaderTitle}\">
                                <ScrollViewer VerticalScrollBarVisibility=\"Auto\">
                                    <StackPanel Spacing=\"16\" Margin=\"0,8,0,0\">
                                        <Grid ColumnDefinitions=\"*,Auto\" ColumnSpacing=\"10\">
                                            <StackPanel Spacing=\"3\">
                                                <TextBlock Text=\"{Binding ExtensionTypeTitle}\" Classes=\"WorkspaceSectionTitle\" FontSize=\"15\"/>
                                                <TextBlock Text=\"{Binding ExtensionSupportText}\" Classes=\"WorkspaceSecondary\" FontSize=\"11.5\" TextWrapping=\"Wrap\"/>
                                            </StackPanel>
                                            <Button Grid.Column=\"1\" Content=\"{Binding ExtensionFolderButtonText}\" Classes=\"WorkspaceSecondary\" Command=\"{Binding OpenPluginsFolderCommand}\" IsVisible=\"{Binding IsExtensionSupported}\"/>
                                        </Grid>

                                        <StackPanel Spacing=\"7\" IsVisible=\"{Binding IsExtensionSupported}\">
                                            <TextBlock Text=\"{Binding InstalledPlugins.Count, StringFormat='{}Installed ({0})'}\" Classes=\"WorkspaceLabel\"/>
                                            <ItemsControl ItemsSource=\"{Binding InstalledPlugins}\">
                                                <ItemsControl.ItemTemplate>
                                                    <DataTemplate x:DataType=\"vm:InstalledServerPlugin\">
                                                        <Border Background=\"#0F171D\" BorderBrush=\"#293844\" BorderThickness=\"1\" CornerRadius=\"7\" Padding=\"10\" Margin=\"0 0 0 6\">
                                                            <Grid ColumnDefinitions=\"*,Auto\" ColumnSpacing=\"10\">
                                                                <StackPanel Spacing=\"2\">
                                                                    <TextBlock Text=\"{Binding Name}\" Classes=\"WorkspaceLabel\"/>
                                                                    <TextBlock Text=\"{Binding FileName}\" Classes=\"WorkspaceSecondary\" FontSize=\"10.5\"/>
                                                                </StackPanel>
                                                                <Button Grid.Column=\"1\" Content=\"Remove\" Classes=\"WorkspaceDanger\"
                                                                        Command=\"{Binding $parent[UserControl].((vm:CreateServerViewModel)DataContext).RemovePluginCommand}\"
                                                                        CommandParameter=\"{Binding}\"
                                                                        IsEnabled=\"{Binding !$parent[UserControl].((vm:CreateServerViewModel)DataContext).SelectedServer.IsRunning}\"/>
                                                            </Grid>
                                                        </Border>
                                                    </DataTemplate>
                                                </ItemsControl.ItemTemplate>
                                            </ItemsControl>
                                        </StackPanel>

                                        <Grid ColumnDefinitions=\"*,Auto\" ColumnSpacing=\"8\" IsVisible=\"{Binding IsExtensionSupported}\">
                                            <TextBox Text=\"{Binding PluginSearchQuery}\" PlaceholderText=\"{Binding ExtensionSearchPlaceholder}\" Classes=\"WorkspaceField\"/>
                                            <Button Grid.Column=\"1\" Content=\"Search\" Classes=\"WorkspacePrimary\" Command=\"{Binding SearchPluginsCommand}\" IsEnabled=\"{Binding !IsPluginBusy}\"/>
                                        </Grid>
                                        <TextBlock Text=\"{Binding PluginStatusText}\" Classes=\"WorkspaceSecondary\" FontSize=\"11\" TextWrapping=\"Wrap\"/>

                                        <ItemsControl ItemsSource=\"{Binding PluginResults}\" IsVisible=\"{Binding IsExtensionSupported}\">
                                            <ItemsControl.ItemTemplate>
                                                <DataTemplate x:DataType=\"vm:ServerPluginSearchItem\">
                                                    <Border Background=\"#0F171D\" BorderBrush=\"#293844\" BorderThickness=\"1\" CornerRadius=\"7\" Padding=\"11\" Margin=\"0 0 0 7\">
                                                        <Grid ColumnDefinitions=\"*,Auto\" ColumnSpacing=\"12\">
                                                            <StackPanel Spacing=\"3\">
                                                                <StackPanel Orientation=\"Horizontal\" Spacing=\"8\">
                                                                    <TextBlock Text=\"{Binding Name}\" Classes=\"WorkspaceLabel\"/>
                                                                    <TextBlock Text=\"{Binding Author}\" Classes=\"WorkspaceSecondary\" FontSize=\"11\"/>
                                                                    <TextBlock Text=\"{Binding DownloadsLabel}\" Classes=\"WorkspaceSecondary\" FontSize=\"11\" Foreground=\"#76A9C8\"/>
                                                                </StackPanel>
                                                                <TextBlock Text=\"{Binding Description}\" Classes=\"WorkspaceSecondary\" FontSize=\"11\" TextWrapping=\"Wrap\" MaxLines=\"2\"/>
                                                            </StackPanel>
                                                            <Button Grid.Column=\"1\" Content=\"{Binding ActionLabel}\" Classes=\"ServerActionButton\"
                                                                    Command=\"{Binding $parent[UserControl].((vm:CreateServerViewModel)DataContext).InstallPluginCommand}\"
                                                                    CommandParameter=\"{Binding}\" IsEnabled=\"{Binding CanInstall}\" VerticalAlignment=\"Center\"/>
                                                        </Grid>
                                                    </Border>
                                                </DataTemplate>
                                            </ItemsControl.ItemTemplate>
                                        </ItemsControl>
                                    </StackPanel>
                                </ScrollViewer>
                            </TabItem>

                            <!-- Settings Tab -->
                            <TabItem Header=\"Settings\">
                                <ScrollViewer VerticalScrollBarVisibility=\"Auto\">
                                    <StackPanel Spacing=\"16\" Margin=\"0,8,0,0\">
                                        <Border Classes=\"ServerHintCard\">
                                            <StackPanel Spacing=\"12\">
                                                <Grid ColumnDefinitions=\"*,*,120\" ColumnSpacing=\"12\">
                                                    <StackPanel Spacing=\"7\"><TextBlock Text=\"Minecraft version\" Classes=\"WorkspaceLabel\"/><TextBox Text=\"{Binding EditMinecraftVersion}\" Classes=\"WorkspaceField\" IsEnabled=\"{Binding !SelectedServer.IsRunning}\"/></StackPanel>
                                                    <StackPanel Grid.Column=\"1\" Spacing=\"7\"><TextBlock Text=\"Server software\" Classes=\"WorkspaceLabel\"/><ComboBox ItemsSource=\"{Binding ServerTypes}\" SelectedItem=\"{Binding EditServerType}\" Classes=\"WorkspaceField\" IsEnabled=\"{Binding !SelectedServer.IsRunning}\"/></StackPanel>
                                                    <StackPanel Grid.Column=\"2\" Spacing=\"7\"><TextBlock Text=\"RAM (GB)\" Classes=\"WorkspaceLabel\"/><NumericUpDown Minimum=\"1\" Maximum=\"32\" Value=\"{Binding EditMemoryGb}\" Classes=\"WorkspaceField\" IsEnabled=\"{Binding !SelectedServer.IsRunning}\"/></StackPanel>
                                                </Grid>
                                                <Grid ColumnDefinitions=\"*,Auto\" ColumnSpacing=\"12\">
                                                    <StackPanel Spacing=\"3\">
                                                        <TextBlock Text=\"{Binding AccountModeText}\" Classes=\"WorkspaceLabel\" Foreground=\"#68DDA8\"/>
                                                        <TextBlock Text=\"Offline mode is easier for friends, but usernames are not verified on public servers.\" Classes=\"WorkspaceSecondary\" FontSize=\"10.5\" TextWrapping=\"Wrap\"/>
                                                        <TextBlock Text=\"Paper/Purpur gets ViaVersion compatibility. Fabric, Forge, NeoForge &amp; Quilt support mods.\" Classes=\"WorkspaceSecondary\" FontSize=\"11\" TextWrapping=\"Wrap\"/>
                                                    </StackPanel>
                                                    <TextBlock Grid.Column=\"1\" Text=\"Software downloads automatically on Start\" Classes=\"WorkspaceSecondary\" Foreground=\"#82B5FF\" VerticalAlignment=\"Center\"/>
                                                </Grid>
                                                <CheckBox Content=\"I accept the Minecraft server EULA\" IsChecked=\"{Binding EditEulaAccepted}\" IsVisible=\"{Binding !EditEulaAccepted}\" FontFamily=\"Inter\" FontSize=\"13\" Foreground=\"#E7C783\"/>
                                            </StackPanel>
                                        </Border>

                                        <Border Background=\"#0C151C\" BorderBrush=\"#1C2F40\" BorderThickness=\"1\" CornerRadius=\"8\" Padding=\"16\">
                                            <StackPanel Spacing=\"12\">
                                                <TextBlock Text=\"Advanced Properties\" Classes=\"WorkspaceSectionTitle\" FontSize=\"15\"/>
                                                <Grid ColumnDefinitions=\"*,*\" ColumnSpacing=\"12\">
                                                    <StackPanel Spacing=\"7\"><TextBlock Text=\"Server name\" Classes=\"WorkspaceLabel\"/><TextBox Text=\"{Binding EditServerName}\" Classes=\"WorkspaceField\"/></StackPanel>
                                                    <StackPanel Grid.Column=\"1\" Spacing=\"7\"><TextBlock Text=\"Port\" Classes=\"WorkspaceLabel\"/><NumericUpDown Minimum=\"1024\" Maximum=\"65535\" Value=\"{Binding EditPort}\" Classes=\"WorkspaceField\"/></StackPanel>
                                                </Grid>
                                                <Grid ColumnDefinitions=\"*,*\" ColumnSpacing=\"12\">
                                                    <StackPanel Spacing=\"7\"><TextBlock Text=\"Max players\" Classes=\"WorkspaceLabel\"/><NumericUpDown Minimum=\"1\" Maximum=\"200\" Value=\"{Binding EditMaxPlayers}\" Classes=\"WorkspaceField\"/></StackPanel>
                                                    <StackPanel Grid.Column=\"1\" Spacing=\"7\"><TextBlock Text=\"View distance\" Classes=\"WorkspaceLabel\"/><NumericUpDown Minimum=\"2\" Maximum=\"32\" Value=\"{Binding EditViewDistance}\" Classes=\"WorkspaceField\"/></StackPanel>
                                                </Grid>
                                                <StackPanel Spacing=\"7\"><TextBlock Text=\"Message of the day\" Classes=\"WorkspaceLabel\"/><TextBox Text=\"{Binding EditMotd}\" Classes=\"WorkspaceField\"/></StackPanel>
                                                <StackPanel Spacing=\"7\"><TextBlock Text=\"Launch JAR\" Classes=\"WorkspaceLabel\"/><TextBox Text=\"{Binding EditLaunchJarName}\" Classes=\"WorkspaceField\"/></StackPanel>
                                                <CheckBox Content=\"Only allow premium / Microsoft-authenticated accounts\" IsChecked=\"{Binding EditOnlineMode}\" FontFamily=\"Inter\" FontSize=\"13\" Foreground=\"#D3D8DC\"/>
                                                <CheckBox Content=\"I accept the Minecraft server EULA\" IsChecked=\"{Binding EditEulaAccepted}\" FontFamily=\"Inter\" FontSize=\"13\" Foreground=\"#D3D8DC\"/>
                                                <Button Content=\"Save advanced settings\" Classes=\"WorkspacePrimary\" Command=\"{Binding SaveSelectedServerCommand}\" IsEnabled=\"{Binding !SelectedServer.IsRunning}\"/>
                                            </StackPanel>
                                        </Border>

                                        <Border Classes=\"ServerSetupNotice\" IsVisible=\"{Binding IsDeleteConfirmationVisible}\">
                                            <StackPanel Spacing=\"10\">
                                                <TextBlock Text=\"Delete this server permanently?\" Classes=\"WorkspaceLabel\" Foreground=\"#FF9AA7\"/>
                                                <TextBlock Text=\"The world, plugins, settings and all files in this server folder will be deleted. This cannot be undone.\" Classes=\"WorkspaceSecondary\" FontSize=\"11.5\" TextWrapping=\"Wrap\"/>
                                                <Grid ColumnDefinitions=\"*,*\" ColumnSpacing=\"8\">
                                                    <Button Content=\"Cancel\" Classes=\"WorkspaceSecondary\" Command=\"{Binding CancelDeleteSelectedServerCommand}\"/>
                                                    <Button Grid.Column=\"1\" Content=\"Yes, delete permanently\" Classes=\"WorkspaceDanger\" Command=\"{Binding ConfirmDeleteSelectedServerCommand}\"/>
                                                </Grid>
                                            </StackPanel>
                                        </Border>
                                        
                                        <Grid ColumnDefinitions=\"*,Auto\" ColumnSpacing=\"10\" Margin=\"0,10,0,0\">
                                            <Button Content=\"Open server folder\" Classes=\"WorkspaceSecondary\" Command=\"{Binding OpenSelectedServerFolderCommand}\" HorizontalAlignment=\"Stretch\"/>
                                            <Button Grid.Column=\"1\" Content=\"Delete server\" Classes=\"WorkspaceDanger\" Command=\"{Binding RequestDeleteSelectedServerCommand}\"/>
                                        </Grid>
                                    </StackPanel>
                                </ScrollViewer>
                            </TabItem>
                        </TabControl>
                    </Grid>
                </Border>
\n\"\"\"

lines = lines[:start_idx] + [new_content] + lines[end_idx:]

with open(r'c:\A - WEBSITE\UB LAUNCHER\Launcher.UI\Views\CreateServerView.axaml', 'w', encoding='utf-8') as f:
    f.writelines(lines)

print('Success')
