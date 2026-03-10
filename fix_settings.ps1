$content = [IO.File]::ReadAllText('DiskChecker.UI.Avalonia\Views\SettingsView.axaml')
$old = 'IsChecked="{Binding $parent[ItemsControl].((vm:SettingsViewModel)DataContext).SelectedBackup, 
                                                                            Converter={x:Static ObjectConverters.IsNotNull},
                                                                            ConverterParameter={Binding}}"'
$new = 'IsChecked="{Binding $parent[ItemsControl].DataContext.SelectedBackup, 
                                                                            Converter={x:Static ObjectConverters.IsNotNull},
                                                                            ConverterParameter={Binding}}"'
$content = $content.Replace($old, $new)
[IO.File]::WriteAllText('DiskChecker.UI.Avalonia\Views\SettingsView.axaml', $content)
Write-Host "Fixed SettingsView.axaml"