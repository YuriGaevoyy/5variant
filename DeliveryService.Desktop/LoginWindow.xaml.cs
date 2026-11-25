using System.Windows;
using System.Windows.Input;

namespace DeliveryService.Desktop;

public partial class LoginWindow : Window
{
    public LoginWindow() => InitializeComponent();

    private void SignIn_Click(object sender, RoutedEventArgs e) => SignIn();

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            SignIn();
    }

    private void SignIn()
    {
        if (DataContext is LoginViewModel model)
            model.SignIn.Execute(() => PasswordBox.Password);
    }
}
